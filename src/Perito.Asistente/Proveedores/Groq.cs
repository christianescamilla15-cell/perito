using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Perito.Asistente.Proveedores;

/// <summary>
/// Una llave de API que no se deja imprimir: <see cref="ToString"/> la oculta, no se serializa y los errores al leerla
/// nunca incluyen su valor. Sólo la fábrica del proveedor la revela, para el encabezado de autorización.
/// </summary>
public sealed class SecretoApi
{
    private readonly string _valor;

    private SecretoApi(string valor) => _valor = valor;

    /// <summary>Crea el secreto desde un valor ya leído (pruebas y composición).</summary>
    public static SecretoApi De(string valor) =>
        string.IsNullOrWhiteSpace(valor) ? throw new ArgumentException("Llave vacía.", nameof(valor)) : new(valor.Trim());

    /// <summary>
    /// Lee <c>NOMBRE=valor</c> de un archivo de una línea (p. ej. ~/.config/perito/groq.env). En Linux y macOS exige
    /// permisos 600: si el grupo u otros pueden leerlo, se niega.
    /// </summary>
    public static SecretoApi LeerDeArchivo(string ruta, string nombre)
    {
        if (!File.Exists(ruta))
        {
            throw new FileNotFoundException($"No existe el archivo de la llave: {ruta}");
        }

        if (!OperatingSystem.IsWindows() &&
            (File.GetUnixFileMode(ruta) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead |
                                           UnixFileMode.OtherWrite)) != 0)
        {
            throw new InvalidOperationException($"El archivo de la llave {ruta} debe tener permisos 600.");
        }

        var valor = File.ReadLines(ruta).Select(l => l.Trim())
            .Where(l => l.StartsWith(nombre + "=", StringComparison.Ordinal))
            .Select(l => l[(nombre.Length + 1)..].Trim().Trim('"', '\''))
            .FirstOrDefault(v => v.Length > 0);
        return valor is not null
            ? new SecretoApi(valor)
            : throw new InvalidOperationException($"El archivo {ruta} no tiene una línea {nombre}=… con valor.");
    }

    /// <summary>El valor, sólo para el encabezado de autorización.</summary>
    internal string Revelar() => _valor;

    /// <summary>Si el texto contiene la llave (guarda antes de escribir cualquier salida), sin revelarla.</summary>
    public bool ApareceEn(string texto) => texto.Contains(_valor, StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => "[secreto]";
}

/// <summary>
/// Reintentos de la capa gratuita: respeta <c>retry-after</c> de los 429 y espera creciente en los 5xx, sin pasar de
/// un tope por espera. Si el servidor pide esperar más que el tope (límite diario agotado), no reintenta: falla rápido
/// con el 429 para que la corrida se pueda reanudar después.
/// </summary>
public sealed class PoliticaDeReintentos(int intentos, TimeSpan esperaMaxima) : ClientRetryPolicy(intentos)
{
    /// <inheritdoc />
    protected override bool ShouldRetry(PipelineMessage message, Exception? exception) =>
        base.ShouldRetry(message, exception) && (EsperaPedida(message) is not { } pedida || pedida <= esperaMaxima);

    /// <inheritdoc />
    protected override ValueTask<bool> ShouldRetryAsync(PipelineMessage message, Exception? exception) =>
        ValueTask.FromResult(ShouldRetry(message, exception));

    /// <inheritdoc />
    protected override TimeSpan GetNextDelay(PipelineMessage message, int tryCount)
    {
        var base_ = TimeSpan.FromSeconds(Math.Min(60, 2 * Math.Pow(2, tryCount - 1)));
        var pedida = EsperaPedida(message) ?? TimeSpan.Zero;
        var espera = pedida > base_ ? pedida + TimeSpan.FromMilliseconds(250) : base_;
        return espera > esperaMaxima ? esperaMaxima : espera;
    }

    private static TimeSpan? EsperaPedida(PipelineMessage message)
    {
        if (message.Response is not { } r)
        {
            return null;
        }

        if (r.Headers.TryGetValue("retry-after-ms", out var ms) &&
            double.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out var milisegundos))
        {
            return TimeSpan.FromMilliseconds(milisegundos);
        }

        return r.Headers.TryGetValue("retry-after", out var s) &&
               double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var segundos)
            ? TimeSpan.FromSeconds(segundos)
            : null;
    }
}

/// <summary>
/// Sin ráfagas: deja pasar una solicitud a la vez, separadas al menos 60/RPM segundos, y no rebasa los tokens por
/// minuto de una ventana deslizante (estimados antes de enviar y corregidos con el uso que devuelve el servidor).
/// El reloj y la espera se inyectan para probarlo sin dormir.
/// </summary>
public sealed class ChatConRitmo : IChatCompletionService
{
    private readonly IChatCompletionService _interno;
    private readonly int _solicitudesPorMinuto;
    private readonly int _tokensPorMinuto;
    private readonly Func<double> _segundos;
    private readonly Func<TimeSpan, CancellationToken, Task> _esperar;
    private readonly SemaphoreSlim _turno = new(1, 1);
    private readonly List<(double Marca, int Tokens)> _ventana = [];

    /// <summary>Con el reloj y la espera reales.</summary>
    public ChatConRitmo(IChatCompletionService interno, int solicitudesPorMinuto, int tokensPorMinuto)
        : this(interno, solicitudesPorMinuto, tokensPorMinuto, () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency,
            Task.Delay)
    {
    }

    /// <summary>Con reloj (segundos) y espera inyectados.</summary>
    public ChatConRitmo(IChatCompletionService interno, int solicitudesPorMinuto, int tokensPorMinuto,
        Func<double> segundos, Func<TimeSpan, CancellationToken, Task> esperar)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(solicitudesPorMinuto, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(tokensPorMinuto, 1);
        (_interno, _solicitudesPorMinuto, _tokensPorMinuto, _segundos, _esperar) =
            (interno, solicitudesPorMinuto, tokensPorMinuto, segundos, esperar);
    }

    /// <summary>Total de segundos esperados por el ritmo (para el informe).</summary>
    public double SegundosEsperados { get; private set; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Attributes => _interno.Attributes;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        await _turno.WaitAsync(cancellationToken);
        try
        {
            var estimado = Estimar(chatHistory);
            await EsperarCupoAsync(Math.Min(estimado, _tokensPorMinuto), cancellationToken);
            var marca = _segundos();
            _ventana.Add((marca, estimado));
            var respuesta = await _interno.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel,
                cancellationToken);
            if (respuesta.Select(Uso.De).FirstOrDefault(u => u is not null) is { } uso)
            {
                // Los tokens servidos de la caché de prompts no cuentan para el límite (documentación de Groq).
                _ventana[^1] = (marca, uso.Entrada - uso.EnCache + uso.Salida);
            }

            return respuesta;
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var m in await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken))
        {
            yield return new StreamingChatMessageContent(m.Role, m.Content);
        }
    }

    /// <summary>Tokens que costará la solicitud: ~1 por cada 3.5 caracteres de la historia, más la salida esperada.</summary>
    public static int Estimar(ChatHistory historia) =>
        (int)Math.Ceiling(historia.Sum(m => (m.Content?.Length ?? 0) +
                                            m.Items.OfType<FunctionResultContent>().Sum(f => f.Result?.ToString()?.Length ?? 0))
                          / 3.5) + 600;

    private async Task EsperarCupoAsync(int tokens, CancellationToken ct)
    {
        var separacion = 60.0 / _solicitudesPorMinuto;
        while (true)
        {
            var ahora = _segundos();
            _ventana.RemoveAll(x => ahora - x.Marca >= 60);
            var espera = 0.0;
            if (_ventana.Count > 0)
            {
                espera = Math.Max(espera, _ventana[^1].Marca + separacion - ahora);
            }

            if (_ventana.Count >= _solicitudesPorMinuto || _ventana.Sum(x => x.Tokens) + tokens > _tokensPorMinuto)
            {
                espera = Math.Max(espera, _ventana[0].Marca + 60 - ahora);
            }

            if (espera <= 0)
            {
                return;
            }

            SegundosEsperados += espera;
            await _esperar(TimeSpan.FromSeconds(espera), ct);
        }
    }
}

/// <summary>Tokens que reporta el servidor en los metadatos de la respuesta (OpenAI o Microsoft.Extensions.AI).</summary>
public static class Uso
{
    /// <summary>Entrada (incluye la servida de caché), de ella la servida de caché, y salida, si el conector las
    /// reporta.</summary>
    public static (int Entrada, int EnCache, int Salida)? De(ChatMessageContent respuesta) =>
        respuesta.Metadata?.GetValueOrDefault("Usage") switch
        {
            OpenAI.Chat.ChatTokenUsage u => (u.InputTokenCount, u.InputTokenDetails?.CachedTokenCount ?? 0,
                u.OutputTokenCount),
            Microsoft.Extensions.AI.UsageDetails d => ((int)(d.InputTokenCount ?? 0), (int)(d.CachedInputTokenCount ?? 0),
                (int)(d.OutputTokenCount ?? 0)),
            _ => null,
        };
}

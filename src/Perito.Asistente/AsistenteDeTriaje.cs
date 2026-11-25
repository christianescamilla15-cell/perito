using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Puertos;
using Perito.Asistente.Proveedores;
using Perito.Asistente.Recuperacion;

namespace Perito.Asistente;

/// <summary>
/// La única función que el modelo puede llamar: la carátula de la póliza DEL SINIESTRO que se tría, vía una consulta
/// de Application. No recibe parámetros, así que el modelo no puede pedir otra póliza; devuelve vigencia y coberturas,
/// sin datos personales ni folio.
/// </summary>
public sealed class FuncionesDePoliza(IMediador mediador, Guid siniestroId)
{
    /// <summary>Opciones de JSON de las respuestas a funciones.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>La carátula del siniestro en JSON.</summary>
    [KernelFunction(Prompt.Funcion)]
    [Description("Coberturas contratadas de la póliza del siniestro que se tría, con suma asegurada, deducible y " +
                 "coaseguro, y su vigencia.")]
    public async Task<string> CoberturasContratadasAsync(CancellationToken ct = default)
    {
        var caratula = await mediador.EnviarAsync(new ConsultarCoberturasDelSiniestro(siniestroId), ct);
        return caratula is null ? "{\"error\":\"sin póliza\"}" : JsonSerializer.Serialize(caratula, Json);
    }
}

/// <summary>Opciones del asistente.</summary>
public sealed record OpcionesDelAsistente
{
    /// <summary>«extractivo» (línea base determinista, la de las pruebas y el CI) u «ollama».</summary>
    public string Proveedor { get; init; } = "extractivo";

    /// <summary>Modelo de Ollama (sólo con el proveedor «ollama»).</summary>
    public string Modelo { get; init; } = "qwen2.5:1.5b";

    /// <summary>Dirección de Ollama.</summary>
    public string Endpoint { get; init; } = "http://127.0.0.1:11434";

    /// <summary>Rondas máximas de llamada a funciones antes de exigir la respuesta final.</summary>
    public int RondasMaximas { get; init; } = 3;

    /// <summary>Tiempo máximo por propuesta.</summary>
    public TimeSpan TiempoMaximo { get; init; } = TimeSpan.FromMinutes(3);
}

/// <summary>
/// El asistente de triaje: recupera cláusulas (RAG), arma el prompt, deja que el modelo llame SÓLO a la función de la
/// póliza (lista blanca: cualquier otra se rechaza y se anota) y lee la respuesta JSON. No escribe nada: devuelve una
/// propuesta que Application valida y guarda.
/// </summary>
public sealed class AsistenteDeTriaje(
    IChatCompletionService chat, IRecuperadorDeClausulas recuperador, IMediador mediador, OpcionesDelAsistente opciones)
    : IAsistenteDeTriaje
{
    /// <summary>Las funciones permitidas, con el nombre completo que usa el kernel.</summary>
    public static readonly IReadOnlySet<string> Permitidas = new HashSet<string>(StringComparer.Ordinal)
    {
        $"{Prompt.Complemento}-{Prompt.Funcion}",
    };

    /// <summary>El kernel de un siniestro: el servicio de chat y la función de la póliza acotada a ese siniestro.</summary>
    public static Kernel CrearKernel(IChatCompletionService chat, IMediador mediador, Guid siniestroId)
    {
        var constructor = Kernel.CreateBuilder();
        constructor.Services.AddSingleton(chat);
        var kernel = constructor.Build();
        kernel.Plugins.AddFromObject(new FuncionesDePoliza(mediador, siniestroId), Prompt.Complemento);
        return kernel;
    }

    /// <inheritdoc />
    public async Task<PropuestaGenerada> ProponerAsync(SolicitudAlAsistente solicitud, CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(opciones.TiempoMaximo);
        var contexto = recuperador.Buscar(solicitud.Descripcion.Texto, solicitud.Ramo);
        var recuperadas = contexto.Todos.Select(f => f.Id).ToList();
        var observaciones = new List<string>();
        var kernel = CrearKernel(chat, mediador, solicitud.SiniestroId);
        var historia = new ChatHistory(Prompt.Sistema);
        historia.AddUserMessage(Prompt.Mensaje(solicitud, contexto));
        var ajustes = Ejecucion.Ajustes(opciones);

        string? final = null;
        for (var ronda = 0; ronda <= opciones.RondasMaximas && final is null; ronda++)
        {
            var respuesta = await chat.GetChatMessageContentAsync(historia, ajustes, kernel, limite.Token);
            var llamadas = FunctionCallContent.GetFunctionCalls(respuesta).ToList();
            if (llamadas.Count == 0)
            {
                final = respuesta.Content ?? string.Empty;
                break;
            }

            historia.Add(respuesta);
            foreach (var llamada in llamadas)
            {
                var funcion = Resolver(kernel, llamada);
                var nombre = funcion is null ? $"{llamada.PluginName}-{llamada.FunctionName}".TrimStart('-')
                    : $"{funcion.PluginName}-{funcion.Name}";
                if (funcion is not null && Permitidas.Contains(nombre))
                {
                    var resultado = await funcion.InvokeAsync(kernel, new KernelArguments(), limite.Token);
                    historia.Add(new FunctionResultContent(llamada, resultado).ToChatMessage());
                }
                else
                {
                    observaciones.Add($"función no permitida: {nombre}");
                    historia.Add(new FunctionResultContent(llamada, "función no permitida").ToChatMessage());
                }
            }
        }

        if (final is null)
        {
            observaciones.Add("el modelo no dio respuesta final dentro de las rondas permitidas");
        }

        var leida = LeerRespuesta(final ?? string.Empty, observaciones);
        return new PropuestaGenerada(opciones.Proveedor, Ejecucion.NombreDelModelo(opciones), leida.Tipo,
            leida.Clausula, leida.Datos, leida.Exclusiones, leida.Senales, leida.Justificacion, recuperadas,
            observaciones, reloj.ElapsedMilliseconds);
    }

    /// <summary>
    /// La función del kernel que pide el modelo. Con los conectores de SK llega como complemento + función; con el
    /// adaptador de IChatClient (OllamaSharp) llega sólo el nombre «complemento_funcion» (defecto encontrado por
    /// OllamaProtocoloTests: la lista blanca la rechazaba y el modelo nunca recibía la carátula).
    /// </summary>
    private static KernelFunction? Resolver(Kernel kernel, FunctionCallContent llamada)
    {
        if (kernel.Plugins.TryGetFunction(llamada.PluginName, llamada.FunctionName, out var funcion))
        {
            return funcion;
        }

        return kernel.Plugins.SelectMany(p => p.Select(f => (p.Name, Funcion: f)))
            .Where(x => llamada.FunctionName == $"{x.Name}_{x.Funcion.Name}" ||
                        llamada.FunctionName == $"{x.Name}-{x.Funcion.Name}")
            .Select(x => x.Funcion).FirstOrDefault();
    }

    /// <summary>Lee el JSON de la respuesta con tolerancia: el primer objeto del texto, campos opcionales.</summary>
    public static (string? Tipo, string? Clausula, IReadOnlyList<string> Datos, IReadOnlyList<string> Exclusiones,
        IReadOnlyList<string> Senales, string Justificacion) LeerRespuesta(string texto, List<string> observaciones)
    {
        var inicio = texto.IndexOf('{', StringComparison.Ordinal);
        var fin = texto.LastIndexOf('}');
        if (inicio < 0 || fin <= inicio)
        {
            observaciones.Add("la respuesta no trae un objeto JSON");
            return (null, null, [], [], [], string.Empty);
        }

        try
        {
            using var doc = JsonDocument.Parse(texto[inicio..(fin + 1)]);
            var raiz = doc.RootElement;
            return (Cadena(raiz, "tipo"), Cadena(raiz, "clausula"), Lista(raiz, "datos_faltantes"),
                Lista(raiz, "exclusiones"), Lista(raiz, "senales"), Cadena(raiz, "justificacion") ?? string.Empty);
        }
        catch (JsonException e)
        {
            observaciones.Add(string.Create(CultureInfo.InvariantCulture,
                $"JSON inválido en la posición {e.BytePositionInLine}"));
            return (null, null, [], [], [], string.Empty);
        }
    }

    private static string? Cadena(JsonElement raiz, string nombre) =>
        raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty(nombre, out var v) &&
        v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static IReadOnlyList<string> Lista(JsonElement raiz, string nombre) =>
        raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty(nombre, out var v) &&
        v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim())
                .Where(s => s.Length > 0).ToList()
            : [];
}

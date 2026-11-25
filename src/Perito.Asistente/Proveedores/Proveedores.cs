using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OllamaSharp;
using OpenAI;
using Perito.Application.Abstracciones;
using Perito.Application.Puertos;
using Perito.Asistente.Recuperacion;

namespace Perito.Asistente.Proveedores;

/// <summary>
/// Proveedores intercambiables del servicio de chat, todos detrás de IChatCompletionService: «extractivo» (pruebas y
/// CI, determinista), «ollama» (local) y «groq» (API compatible con OpenAI, con el conector OpenAI de SK). Ningún otro
/// está incluido ni probado.
/// </summary>
public static class Fabrica
{
    /// <summary>El API compatible con OpenAI de Groq.</summary>
    public const string EndpointDeGroq = "https://api.groq.com/openai/v1";

    /// <summary>Crea el servicio de chat del proveedor configurado.</summary>
    public static IChatCompletionService Chat(OpcionesDelAsistente opciones) => opciones.Proveedor switch
    {
        "extractivo" => new ModeloExtractivo(),
        // El conector de Ollama de SK (alfa) recomienda el cliente de OllamaSharp como IChatClient adaptado a SK.
        "ollama" => new ServicioOllama(new OllamaApiClient(new Uri(opciones.Endpoint), opciones.Modelo)),
        "groq" => Groq(opciones),
        _ => throw new InvalidOperationException(
            $"Proveedor «{opciones.Proveedor}» no soportado: use «extractivo», «ollama» o «groq».")
    };

    /// <summary>
    /// Groq por el conector OpenAI de SK con endpoint propio. Reintentos que respetan retry-after con tope (la capa
    /// gratuita devuelve 429) y, si se da, un cliente HTTP propio (pruebas contra un servidor falso).
    /// </summary>
    private static IChatCompletionService Groq(OpcionesDelAsistente opciones)
    {
        var llave = opciones.Llave
                    ?? throw new InvalidOperationException("Falta la llave de Groq (GROQ_API_KEY en ~/.config/perito/groq.env).");
        var cliente = new OpenAIClientOptions
        {
            Endpoint = new Uri(opciones.Endpoint),
            RetryPolicy = new PoliticaDeReintentos(6, TimeSpan.FromSeconds(90)),
            NetworkTimeout = TimeSpan.FromSeconds(120),
        };
        if (opciones.Transporte is { } http)
        {
            cliente.Transport = new HttpClientPipelineTransport(http);
        }

        return new OpenAIChatCompletionService(opciones.Modelo,
            new OpenAIClient(new ApiKeyCredential(llave.Revelar()), cliente));
    }

    /// <summary>Registra el asistente: recuperador (índice una vez), servicio de chat y el asistente por alcance.</summary>
    public static IServiceCollection AgregarAsistente(this IServiceCollection servicios, OpcionesDelAsistente opciones)
    {
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IRecuperadorDeClausulas>(new RecuperadorBm25());
        var chat = Chat(opciones);
        servicios.AddSingleton(opciones.Proveedor == "groq"
            ? new ChatConRitmo(chat, opciones.SolicitudesPorMinuto, opciones.TokensPorMinuto)
            : chat);
        servicios.AddScoped<IAsistenteDeTriaje>(p => new AsistenteDeTriaje(p.GetRequiredService<IChatCompletionService>(),
            p.GetRequiredService<IRecuperadorDeClausulas>(), p.GetRequiredService<IMediador>(), opciones));
        return servicios;
    }
}

/// <summary>Ajustes de ejecución por proveedor.</summary>
public static class Ejecucion
{
    /// <summary>Temperatura 0 y llamada a funciones sin invocación automática: el asistente decide qué se ejecuta.</summary>
    public static PromptExecutionSettings Ajustes(OpcionesDelAsistente opciones)
    {
        if (opciones.Proveedor == "groq")
        {
            return new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
                Temperature = 0,
                MaxTokens = opciones.TokensMaximosDeSalida,
                ReasoningEffort = opciones.EsfuerzoDeRazonamiento,
            };
        }

        // La temperatura va como dato de extensión: el adaptador de IChatClient la traduce a ChatOptions.
        return new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
            ExtensionData = opciones.Proveedor == "extractivo"
                ? null
                : new Dictionary<string, object> { ["temperature"] = 0.0 },
        };
    }

    /// <summary>
    /// Los ajustes de la respuesta final, con el resultado de la función ya entregado: las funciones siguen declaradas
    /// pero no se pueden llamar (tool_choice «none»).
    /// </summary>
    public static PromptExecutionSettings AjustesFinales(OpcionesDelAsistente opciones)
    {
        var ajustes = Ajustes(opciones);
        ajustes.FunctionChoiceBehavior = FunctionChoiceBehavior.None();
        return ajustes;
    }

    /// <summary>Nombre del modelo que queda en la propuesta.</summary>
    public static string NombreDelModelo(OpcionesDelAsistente opciones) =>
        opciones.Proveedor == "extractivo" ? ModeloExtractivo.Nombre : opciones.Modelo;
}

/// <summary>El servicio de chat de Ollama dueño de su cliente HTTP (lo libera al liberarse el contenedor).</summary>
public sealed class ServicioOllama(OllamaApiClient cliente) : IChatCompletionService, IDisposable
{
    private readonly OllamaApiClient _cliente = cliente;
    private readonly IChatCompletionService _servicio = cliente.AsChatCompletionService();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Attributes => _servicio.Attributes;

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
        CancellationToken cancellationToken = default) =>
        _servicio.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
        CancellationToken cancellationToken = default) =>
        _servicio.GetStreamingChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _cliente.Dispose();
}

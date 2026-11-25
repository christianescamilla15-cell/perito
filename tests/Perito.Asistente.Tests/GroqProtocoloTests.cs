using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Puertos;
using Perito.Application.Siniestros;
using Perito.Application.Triaje;
using Perito.Asistente.Proveedores;
using Perito.Asistente.Recuperacion;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.EnMemoria;

namespace Perito.Asistente.Tests;

/// <summary>
/// Un servidor que habla el protocolo HTTP de OpenAI (/chat/completions), como el API compatible de Groq, con
/// respuestas fijas: pide la función con el nombre que se le indique y luego responde el JSON. No es un modelo.
/// </summary>
internal sealed class OpenAIFalso(Func<string, string>? nombreDevuelto = null) : HttpMessageHandler
{
    public const string Final =
        """{"tipo":"Colision","clausula":"AUT-2.1-a","datos_faltantes":["PresupuestoDeReparacion"],"exclusiones":[],"senales":[],"justificacion":"alcance"}""";

    public List<JsonNode> Solicitudes { get; } = [];

    public List<string?> Autorizaciones { get; } = [];

    /// <summary>Respuestas de error que se devuelven antes de las normales (estado y retry-after en segundos).</summary>
    public Queue<(HttpStatusCode Estado, int? EsperaSegundos)> Errores { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Assert.EndsWith("/chat/completions", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Autorizaciones.Add(request.Headers.Authorization?.ToString());
        var cuerpo = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
        Solicitudes.Add(cuerpo);
        if (Errores.TryDequeue(out var error))
        {
            var r = new HttpResponseMessage(error.Estado)
            {
                Content = new StringContent("""{"error":{"message":"límite de prueba","type":"requests"}}""",
                    Encoding.UTF8, "application/json"),
            };
            if (error.EsperaSegundos is { } s)
            {
                r.Headers.Add("retry-after", s.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return r;
        }

        var hayResultado = cuerpo["messages"]!.AsArray().Any(m => (string?)m!["role"] == "tool");
        var ofrecida = (string?)cuerpo["tools"]?[0]?["function"]?["name"] ?? "";
        JsonObject mensaje = hayResultado
            ? new JsonObject { ["role"] = "assistant", ["content"] = Final }
            : new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = null,
                ["tool_calls"] = new JsonArray(new JsonObject
                {
                    ["id"] = "call_1",
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = (nombreDevuelto ?? (x => x))(ofrecida),
                        ["arguments"] = "{}",
                    },
                }),
            };
        var respuesta = new JsonObject
        {
            ["id"] = "chatcmpl-prueba", ["object"] = "chat.completion", ["created"] = 1764000000,
            ["model"] = (string?)cuerpo["model"],
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0, ["message"] = mensaje, ["finish_reason"] = hayResultado ? "stop" : "tool_calls",
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = 1000, ["completion_tokens"] = 50, ["total_tokens"] = 1050 },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(respuesta.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>
/// El proveedor «groq» por el conector OpenAI de SK contra su protocolo HTTP: la función, los ajustes y su resultado
/// viajan; la lista blanca resuelve las formas en que el nombre puede volver; los 429 se reintentan y un límite diario
/// falla rápido. Va al CI sin red ni llave real.
/// </summary>
public class GroqProtocoloTests
{
    internal const string LlaveFalsa = "gsk_PRUEBA_esto_no_es_una_llave_real_0123456789";

    internal static OpcionesDelAsistente Opciones(HttpMessageHandler servidor) => new()
    {
        Proveedor = "groq",
        Modelo = "openai/gpt-oss-120b",
        Endpoint = "https://groq.prueba/openai/v1",
        Llave = SecretoApi.De(LlaveFalsa),
        Transporte = new HttpClient(servidor),
    };

    private static async Task<PropuestaDeTriaje> ProponerAsync(OpcionesDelAsistente opciones)
    {
        var chat = Fabrica.Chat(opciones);
        var servicios = new ServiceCollection().AgregarPeritoEnMemoria(new AlmacenEnMemoria(),
            new RelojFijo(new DateTimeOffset(2026, 6, 15, 17, 0, 0, TimeSpan.Zero)));
        servicios.AddScoped<IAsistenteDeTriaje>(p =>
            new AsistenteDeTriaje(chat, new RecuperadorBm25(), p.GetRequiredService<IMediador>(), opciones));
        await using var proveedor = servicios.BuildServiceProvider();
        async Task<T> Enviar<T>(ISolicitud<T> s)
        {
            await using var alcance = proveedor.CreateAsyncScope();
            return await alcance.ServiceProvider.GetRequiredService<IMediador>().EnviarAsync(s);
        }

        var titular = await Enviar(new RegistrarAsegurado("Lucía Ferrer Montaño", "FEML850101AB1",
            "lucia.ferrer@example.com", "5541002233", null));
        await Enviar(new EmitirPoliza("AFV-AUT-000088", Ramo.Auto, titular.Valor, new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31),
            [new(CodigoCobertura.DanosMateriales, 350_000m, new Deducible(TipoDeDeducible.PorcentajeSuma, 0.05m), 0m)]));
        var id = (await Enviar(new AvisarSiniestro("AFV-AUT-000088", new DateOnly(2026, 6, 14),
            "Soy Lucía Ferrer Montaño; un camión me alcanzó por detrás en la calle Olivo 24. Tengo fotos.", null,
            null))).Valor;
        return (await Enviar(new ProponerTriaje(id, "ajustador.uno"))).Valor;
    }

    [Fact]
    public async Task Por_el_protocolo_de_OpenAI_viajan_la_funcion_los_ajustes_la_llave_y_el_resultado()
    {
        var servidor = new OpenAIFalso();
        var propuesta = await ProponerAsync(Opciones(servidor));

        Assert.Equal(2, servidor.Solicitudes.Count);
        var primera = servidor.Solicitudes[0];
        Assert.Equal("openai/gpt-oss-120b", (string?)primera["model"]);
        Assert.Equal("poliza-coberturas_contratadas",
            (string?)Assert.Single(primera["tools"]!.AsArray())!["function"]!["name"]);
        Assert.Equal(0.0, (double?)primera["temperature"]);
        Assert.Equal("low", (string?)primera["reasoning_effort"]);
        Assert.Equal(2048, (int?)(primera["max_completion_tokens"] ?? primera["max_tokens"]));
        Assert.All(servidor.Autorizaciones, a => Assert.Equal("Bearer " + LlaveFalsa, a));
        var resultado = servidor.Solicitudes[1]["messages"]!.AsArray().Single(m => (string?)m!["role"] == "tool");
        Assert.Equal("call_1", (string?)resultado!["tool_call_id"]);
        Assert.Contains("DanosMateriales", (string?)resultado["content"], StringComparison.Ordinal);
        foreach (var personal in new[] { "Lucía", "Ferrer", "Olivo 24", "AFV-AUT-000088" })
        {
            Assert.DoesNotContain(personal, string.Join("\n", servidor.Solicitudes.Select(s => s.ToJsonString())),
                StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(("groq", "openai/gpt-oss-120b", TipoDeSiniestro.Colision, "AUT-2.1-a", true),
            (propuesta.Proveedor, propuesta.Modelo, propuesta.Tipo!.Value, propuesta.Clausula, propuesta.CitaSustentada));
    }

    /// <summary>
    /// El defecto de OllamaSharp no se repite: el conector OpenAI ofrece «poliza-coberturas_contratadas» y el modelo
    /// puede devolverla igual, con guion bajo o sin complemento; las tres se ejecutan. Otra función se rechaza.
    /// </summary>
    [Theory]
    [InlineData("poliza-coberturas_contratadas", true)]
    [InlineData("poliza_coberturas_contratadas", true)]
    [InlineData("coberturas_contratadas", true)]
    [InlineData("siniestros-dictaminar", false)]
    public async Task La_lista_blanca_resuelve_las_formas_del_nombre_y_rechaza_lo_demas(string devuelto, bool ejecuta)
    {
        var servidor = new OpenAIFalso(_ => devuelto);
        var propuesta = await ProponerAsync(Opciones(servidor));

        var resultado = (string?)servidor.Solicitudes[1]["messages"]!.AsArray()
            .Single(m => (string?)m!["role"] == "tool")!["content"];
        Assert.Equal(ejecuta, resultado!.Contains("DanosMateriales", StringComparison.Ordinal));
        Assert.Equal(!ejecuta, propuesta.Observaciones.Any(o => o.StartsWith("función no permitida", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Un_429_con_retry_after_corto_se_reintenta_y_la_propuesta_sale()
    {
        var servidor = new OpenAIFalso();
        servidor.Errores.Enqueue((HttpStatusCode.TooManyRequests, 1));
        var propuesta = await ProponerAsync(Opciones(servidor));
        Assert.Equal(3, servidor.Solicitudes.Count);          // el 429, su reintento y la segunda llamada
        Assert.Equal("AUT-2.1-a", propuesta.Clausula);
    }

    [Fact]
    public async Task Un_limite_diario_que_pide_esperar_una_hora_falla_rapido_sin_reintentar()
    {
        var servidor = new OpenAIFalso();
        servidor.Errores.Enqueue((HttpStatusCode.TooManyRequests, 3600));
        var cronometro = System.Diagnostics.Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<HttpOperationException>(() => ProponerAsync(Opciones(servidor)));
        Assert.Equal(HttpStatusCode.TooManyRequests, e.StatusCode);
        Assert.Single(servidor.Solicitudes);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(30));
        Assert.DoesNotContain(LlaveFalsa, e.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Sin_llave_el_proveedor_groq_no_se_crea() =>
        Assert.Throws<InvalidOperationException>(() => Fabrica.Chat(new OpcionesDelAsistente { Proveedor = "groq" }));
}

/// <summary>El ritmo sin ráfagas, con reloj y espera simulados.</summary>
public class ChatConRitmoTests
{
    private sealed class Eco : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ChatMessageContent>>([new ChatMessageContent(AuthorRole.Assistant, "ok")]);

        public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Separa_las_solicitudes_y_respeta_las_solicitudes_y_tokens_por_minuto()
    {
        var reloj = 0.0;
        var esperas = new List<double>();
        var ritmo = new ChatConRitmo(new Eco(), solicitudesPorMinuto: 2, tokensPorMinuto: 1_400, () => reloj,
            (t, _) =>
            {
                esperas.Add(t.TotalSeconds);
                reloj += t.TotalSeconds;
                return Task.CompletedTask;
            });
        var historia = new ChatHistory("x");           // ~601 tokens estimados por solicitud

        for (var i = 0; i < 3; i++)
        {
            await ritmo.GetChatMessageContentsAsync(historia);
        }

        // 2.ª: espera la separación de 30 s; 3.ª: el tope de 2 por minuto (y de tokens) la lleva al segundo 60.
        Assert.Equal(new[] { 30.0, 30.0 }, esperas);
        Assert.Equal(60.0, ritmo.SegundosEsperados);
    }

    [Fact]
    public void Un_ritmo_sin_cupo_no_se_puede_crear() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChatConRitmo(new Eco(), 0, 1));
}

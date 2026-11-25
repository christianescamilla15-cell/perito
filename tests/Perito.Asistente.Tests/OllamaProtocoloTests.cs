using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using OllamaSharp;
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
/// Un servidor que habla el protocolo HTTP de Ollama (/api/chat) con respuestas fijas: primero pide la función que
/// le ofrecieron, después responde el JSON. No es un modelo; prueba que la ruta real del proveedor «ollama»
/// (OllamaSharp como IChatClient adaptado a Semantic Kernel) transporta herramientas, temperatura y resultados.
/// </summary>
internal sealed class OllamaFalso : HttpMessageHandler
{
    public List<JsonNode> Solicitudes { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Assert.EndsWith("/api/chat", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        var cuerpo = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;
        Solicitudes.Add(cuerpo);
        var hayResultado = cuerpo["messages"]!.AsArray().Any(m => (string?)m!["role"] == "tool");
        var mensaje = hayResultado
            ? new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = """{"tipo":"Colision","clausula":"AUT-2.1-a","datos_faltantes":["PresupuestoDeReparacion"],"exclusiones":[],"senales":[],"justificacion":"choque por alcance"}""",
            }
            : new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = "",
                ["tool_calls"] = new JsonArray(new JsonObject
                {
                    ["function"] = new JsonObject
                    {
                        ["name"] = (string?)cuerpo["tools"]![0]!["function"]!["name"],
                        ["arguments"] = new JsonObject(),
                    },
                }),
            };
        var respuesta = new JsonObject
        {
            ["model"] = "modelo-de-prueba", ["created_at"] = "2026-11-25T00:00:00Z", ["message"] = mensaje,
            ["done"] = true, ["done_reason"] = "stop", ["prompt_eval_count"] = 1, ["eval_count"] = 1,
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(respuesta.ToJsonString() + "\n", Encoding.UTF8, "application/x-ndjson"),
        };
    }
}

/// <summary>La ruta del proveedor «ollama» de punta a punta contra el protocolo, sin modelo.</summary>
public class OllamaProtocoloTests
{
    [Fact]
    public async Task Por_el_protocolo_de_Ollama_viajan_la_funcion_la_temperatura_y_su_resultado_sin_datos_personales()
    {
        var ollama = new OllamaFalso();
        using var http = new HttpClient(ollama) { BaseAddress = new Uri("http://ollama.prueba:11434") };
        using var cliente = new OllamaApiClient(http, "modelo-de-prueba");
        IChatCompletionService chat = cliente.AsChatCompletionService();
        var opciones = new OpcionesDelAsistente { Proveedor = "ollama", Modelo = "modelo-de-prueba" };
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
        await Enviar(new EmitirPoliza("AFV-AUT-000077", Ramo.Auto, titular.Valor, new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31),
            [new(CodigoCobertura.DanosMateriales, 350_000m, new Deducible(TipoDeDeducible.PorcentajeSuma, 0.05m), 0m)]));
        var id = (await Enviar(new AvisarSiniestro("AFV-AUT-000077", new DateOnly(2026, 6, 14),
            "Soy Lucía Ferrer Montaño; un camión me alcanzó por detrás en la calle Olivo 24. Tengo fotos.", null,
            null))).Valor;

        var propuesta = (await Enviar(new ProponerTriaje(id, "ajustador.uno"))).Valor;

        Assert.Equal(2, ollama.Solicitudes.Count);
        var primera = ollama.Solicitudes[0];
        var herramienta = Assert.Single(primera["tools"]!.AsArray());
        Assert.Contains("coberturas_contratadas", (string?)herramienta!["function"]!["name"], StringComparison.Ordinal);
        Assert.Equal(0.0, (double?)primera["options"]?["temperature"]);
        var resultado = ollama.Solicitudes[1]["messages"]!.AsArray().Single(m => (string?)m!["role"] == "tool");
        Assert.Contains("DanosMateriales", (string?)resultado!["content"], StringComparison.Ordinal);
        var enviado = string.Join("\n", ollama.Solicitudes.Select(s => s.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        })));
        foreach (var personal in new[] { "Lucía", "Ferrer", "Olivo 24", "FEML", "AFV-AUT-000077" })
        {
            Assert.DoesNotContain(personal, enviado, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(("ollama", "modelo-de-prueba", TipoDeSiniestro.Colision, "AUT-2.1-a"),
            (propuesta.Proveedor, propuesta.Modelo, propuesta.Tipo!.Value, propuesta.Clausula));
    }

    [Fact]
    public void La_fabrica_crea_los_dos_proveedores_probados_y_rechaza_cualquier_otro()
    {
        Assert.IsType<ModeloExtractivo>(Fabrica.Chat(new OpcionesDelAsistente { Proveedor = "extractivo" }));
        Assert.NotNull(Fabrica.Chat(new OpcionesDelAsistente { Proveedor = "ollama" }));
        Assert.Throws<InvalidOperationException>(() => Fabrica.Chat(new OpcionesDelAsistente { Proveedor = "azure-openai" }));
    }
}

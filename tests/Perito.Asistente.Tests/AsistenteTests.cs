using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Puertos;
using Perito.Application.Siniestros;
using Perito.Asistente.Proveedores;
using Perito.Asistente.Recuperacion;
using Perito.Domain.Polizas;
using Perito.Domain.Privacidad;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using Perito.EnMemoria;

namespace Perito.Asistente.Tests;

/// <summary>Servicio de chat con guion: cada llamada responde con la siguiente función del guion.</summary>
internal sealed class ChatGuionado(params Func<ChatHistory, ChatMessageContent>[] turnos) : IChatCompletionService
{
    public List<ChatHistory> Historias { get; } = [];

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        Historias.Add(new ChatHistory(chatHistory));
        var turno = turnos[Math.Min(Historias.Count - 1, turnos.Length - 1)];
        return Task.FromResult<IReadOnlyList<ChatMessageContent>>([turno(chatHistory)]);
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var m in await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken))
        {
            yield return new StreamingChatMessageContent(m.Role, m.Content);
        }
    }

    public static ChatMessageContent Llama(params (string Complemento, string Funcion)[] funciones)
    {
        var m = new ChatMessageContent(AuthorRole.Assistant, content: null);
        foreach (var (complemento, funcion) in funciones)
        {
            m.Items.Add(new FunctionCallContent(funcion, complemento, Guid.NewGuid().ToString("N"), new KernelArguments()));
        }

        return m;
    }

    public static ChatMessageContent Responde(string texto) => new(AuthorRole.Assistant, texto);
}

/// <summary>El asistente con Semantic Kernel: la función por Application, la lista blanca y la lectura de la salida.</summary>
public class AsistenteTests
{
    private const string Final =
        """{"tipo":"Colision","clausula":"AUT-2.1-a","datos_faltantes":["PresupuestoDeReparacion"],"exclusiones":[],"senales":[],"justificacion":"ok"}""";

    private sealed class Montaje : IAsyncDisposable
    {
        private readonly ServiceProvider _proveedor;

        public Montaje(IChatCompletionService chat, IRecuperadorDeClausulas? recuperador = null)
        {
            var servicios = new ServiceCollection()
                .AgregarPeritoEnMemoria(Almacen, new RelojFijo(new DateTimeOffset(2026, 6, 15, 17, 0, 0, TimeSpan.Zero)));
            servicios.AddScoped<IAsistenteDeTriaje>(p => new AsistenteDeTriaje(chat,
                recuperador ?? new RecuperadorBm25(), p.GetRequiredService<IMediador>(), new OpcionesDelAsistente()));
            _proveedor = servicios.BuildServiceProvider();
        }

        public AlmacenEnMemoria Almacen { get; } = new();

        public async Task<T> Enviar<T>(ISolicitud<T> s)
        {
            await using var alcance = _proveedor.CreateAsyncScope();
            return await alcance.ServiceProvider.GetRequiredService<IMediador>().EnviarAsync(s);
        }

        public async Task<Guid> SiniestroAsync()
        {
            var titular = await Enviar(new RegistrarAsegurado("Lucía Ferrer Montaño", "FEML850101AB1",
                "lucia.ferrer@example.com", "5541002233", null));
            await Enviar(new EmitirPoliza("AFV-AUT-000042", Ramo.Auto, titular.Valor, new DateOnly(2026, 1, 1),
                new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31),
                [new(CodigoCobertura.DanosMateriales, 350_000m, new Deducible(TipoDeDeducible.PorcentajeSuma, 0.05m), 0m)]));
            return (await Enviar(new AvisarSiniestro("AFV-AUT-000042", new DateOnly(2026, 6, 14),
                "Soy Lucía Ferrer Montaño; un camión me alcanzó por detrás en la calle Olivo 24 y chocó la defensa.",
                null, null))).Valor;
        }

        public ValueTask DisposeAsync() => _proveedor.DisposeAsync();
    }

    [Fact]
    public async Task La_funcion_de_la_poliza_se_ejecuta_via_Application_y_no_trae_datos_personales()
    {
        var chat = new ChatGuionado(_ => ChatGuionado.Llama(("poliza", "coberturas_contratadas")),
            _ => ChatGuionado.Responde(Final));
        await using var m = new Montaje(chat);
        var id = await m.SiniestroAsync();

        var propuesta = (await m.Enviar(new Application.Triaje.ProponerTriaje(id, "ajustador.uno"))).Valor;

        var resultado = Assert.Single(chat.Historias[^1].SelectMany(x => x.Items.OfType<FunctionResultContent>()));
        var json = Assert.IsType<string>(resultado.Result);
        Assert.Contains("DanosMateriales", json, StringComparison.Ordinal);
        Assert.Contains("2026-12-31", json, StringComparison.Ordinal);
        foreach (var personal in new[] { "Lucía", "Ferrer", "FEML", "lucia.ferrer", "5541002233", "AFV-AUT-000042" })
        {
            Assert.DoesNotContain(personal, json, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal((TipoDeSiniestro.Colision, "AUT-2.1-a", true), (propuesta.Tipo, propuesta.Clausula,
            propuesta.CitaSustentada));
    }

    [Fact]
    public async Task Una_funcion_fuera_de_la_lista_blanca_no_se_ejecuta_y_queda_anotada()
    {
        var chat = new ChatGuionado(
            _ => ChatGuionado.Llama(("siniestros", "dictaminar"), ("tesoreria", "pagar")),
            _ => ChatGuionado.Responde(Final));
        await using var m = new Montaje(chat);
        var id = await m.SiniestroAsync();
        var antes = System.Text.Json.JsonSerializer.Serialize(m.Almacen.SiniestroGuardado(id));

        var propuesta = (await m.Enviar(new Application.Triaje.ProponerTriaje(id, "ajustador.uno"))).Valor;

        Assert.Contains("función no permitida: siniestros-dictaminar", propuesta.Observaciones);
        Assert.Contains("función no permitida: tesoreria-pagar", propuesta.Observaciones);
        Assert.All(chat.Historias[^1].SelectMany(x => x.Items.OfType<FunctionResultContent>()),
            r => Assert.Equal("función no permitida", r.Result));
        Assert.Equal(antes, System.Text.Json.JsonSerializer.Serialize(m.Almacen.SiniestroGuardado(id)));
        Assert.Equal(EstadoSiniestro.Avisado, m.Almacen.SiniestroGuardado(id)!.Estado);
    }

    [Fact]
    public void El_kernel_solo_expone_la_funcion_de_lectura_de_la_poliza()
    {
        var kernel = AsistenteDeTriaje.CrearKernel(new ModeloExtractivo(), new MediadorQueNoSeUsa(), Guid.NewGuid());
        var funciones = kernel.Plugins.SelectMany(p => p.Select(f => $"{p.Name}-{f.Name}")).ToList();
        Assert.Equal(AsistenteDeTriaje.Permitidas.Order(), funciones.Order());
        // Sin parámetros (salvo la cancelación): el modelo no puede pedir otra póliza.
        Assert.DoesNotContain(Assert.Single(kernel.Plugins).Single().Metadata.Parameters,
            p => p.ParameterType != typeof(CancellationToken));
    }

    [Fact]
    public async Task Un_modelo_que_solo_pide_funciones_se_corta_en_las_rondas_permitidas()
    {
        var chat = new ChatGuionado(_ => ChatGuionado.Llama(("poliza", "coberturas_contratadas")));
        await using var m = new Montaje(chat);
        var id = await m.SiniestroAsync();

        var propuesta = (await m.Enviar(new Application.Triaje.ProponerTriaje(id, "ajustador.uno"))).Valor;

        Assert.Equal(new OpcionesDelAsistente().RondasMaximas + 1, chat.Historias.Count);
        Assert.Contains(propuesta.Observaciones, o => o.Contains("no dio respuesta final", StringComparison.Ordinal));
        Assert.Null(propuesta.Tipo);
    }

    [Theory]
    [InlineData("Claro, aquí va: {\"tipo\":\"Vuelco\",\"clausula\":\"AUT-2.1-b\"} ¡Suerte!", "Vuelco")]
    [InlineData("```json\n{\"tipo\":\"Vuelco\",\"datos_faltantes\":\"Lugar\"}\n```", "Vuelco")]
    public void La_respuesta_se_lee_aunque_traiga_texto_alrededor_y_campos_con_otro_tipo(string texto, string tipo)
    {
        var observaciones = new List<string>();
        var r = AsistenteDeTriaje.LeerRespuesta(texto, observaciones);
        Assert.Equal(tipo, r.Tipo);
        Assert.Empty(r.Datos);              // «datos_faltantes» como cadena no es una lista: se ignora
        Assert.Empty(observaciones);
    }

    [Theory]
    [InlineData("No sé qué tipo es.", "no trae un objeto JSON")]
    [InlineData("{\"tipo\": \"Vuelco\", }", "JSON inválido")]
    public void Una_respuesta_ilegible_queda_anotada_y_no_inventa_campos(string texto, string observacion)
    {
        var observaciones = new List<string>();
        var r = AsistenteDeTriaje.LeerRespuesta(texto, observaciones);
        Assert.Null(r.Tipo);
        Assert.Contains(observaciones, o => o.Contains(observacion, StringComparison.Ordinal));
    }

    private sealed class MediadorQueNoSeUsa : IMediador
    {
        public Task<TRespuesta> EnviarAsync<TRespuesta>(ISolicitud<TRespuesta> solicitud, CancellationToken ct = default) =>
            throw new InvalidOperationException("no se esperaba una llamada");
    }
}

/// <summary>La línea base determinista: pide la función, lee sólo el prompt y respeta negaciones.</summary>
public class ModeloExtractivoTests
{
    private static async Task<string> Responder(string descripcion, Ramo ramo, bool conFuncion = false)
    {
        var modelo = new ModeloExtractivo();
        var contexto = new RecuperadorBm25().Buscar(descripcion, ramo);
        var historia = new ChatHistory(Prompt.Sistema);
        historia.AddUserMessage(Prompt.Mensaje(new SolicitudAlAsistente(Guid.NewGuid(), ramo,
            Seudonimizador.Seudonimizar(descripcion, [])), contexto));
        var kernel = conFuncion ? AsistenteDeTriaje.CrearKernel(modelo, null!, Guid.NewGuid()) : null;
        var ajustes = conFuncion ? Ejecucion.Ajustes(new OpcionesDelAsistente()) : null;
        var r = await modelo.GetChatMessageContentAsync(historia, ajustes, kernel);
        return conFuncion ? string.Join(",", FunctionCallContent.GetFunctionCalls(r).Select(f => f.FunctionName)) : r.Content!;
    }

    [Fact]
    public async Task Con_la_funcion_disponible_primero_la_pide() =>
        Assert.Equal("coberturas_contratadas", await Responder("Choqué contra un poste.", Ramo.Auto, conFuncion: true));

    [Fact]
    public async Task Una_negacion_antes_del_indicio_deja_el_dato_como_faltante()
    {
        var json = await Responder("Me robaron la camioneta en la avenida Juárez. Todavía no he ido al Ministerio Público.",
            Ramo.Auto);
        Assert.Contains("\"DenunciaAnteMinisterioPublico\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Lugar\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Que_lleguen_los_bomberos_no_es_su_dictamen()
    {
        var sin = await Responder("Se incendió el coche en el periférico; llegaron los bomberos.", Ramo.Auto);
        var con = await Responder("Se incendió el coche en el periférico; tengo el reporte de bomberos.", Ramo.Auto);
        Assert.Contains("DictamenDeBomberos", sin, StringComparison.Ordinal);
        Assert.DoesNotContain("DictamenDeBomberos", con, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_clausulas_no_propone_tipo_ni_cita()
    {
        var historia = new ChatHistory(Prompt.Sistema);
        historia.AddUserMessage(Prompt.Mensaje(new SolicitudAlAsistente(Guid.NewGuid(), Ramo.Auto,
            Seudonimizador.Seudonimizar("Choqué contra un poste en la calle Olivo.", [])), ContextoRecuperado.Vacio));
        var json = (await new ModeloExtractivo().GetChatMessageContentAsync(historia)).Content!;
        Assert.Contains("\"tipo\":null", json, StringComparison.Ordinal);
        Assert.Contains("\"clausula\":null", json, StringComparison.Ordinal);
    }
}

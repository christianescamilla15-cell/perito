using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Puertos;
using Perito.Application.Siniestros;
using Perito.Application.Triaje;
using Perito.Asistente;
using Perito.Asistente.Proveedores;
using Perito.Asistente.Recuperacion;
using Perito.Domain.Producto;
using Perito.EnMemoria;

namespace Perito.Evaluacion;

/// <summary>Qué contexto recibe el asistente: el recuperado, ninguno o el de otro caso (control negativo).</summary>
public enum Contexto
{
    /// <summary>Lo que recupera BM25 para la descripción.</summary>
    Normal,

    /// <summary>Nada.</summary>
    Vacio,

    /// <summary>Lo recuperado para OTRO caso del mismo ramo con otra cláusula esperada.</summary>
    Barajado,
}

/// <summary>Resultado de un caso.</summary>
public sealed record ResultadoDeCaso(
    string Id, string Esperado, string? Tipo, string? Clausula, IReadOnlyList<string> DatosFaltantes,
    IReadOnlyList<string> Exclusiones, IReadOnlyList<string> Senales, IReadOnlyList<string> Recuperadas,
    bool LlamoFuncion, int PiiEnPrompt, long? LatenciaMs, IReadOnlyList<string> Observaciones, bool TipoCorrecto,
    bool CitaCorrecta, bool DatosExactos, int LlamadasAlModelo = 0, int? TokensDeEntrada = null,
    int? TokensDeSalida = null, IReadOnlyList<long>? LatenciasDelModeloMs = null, int? TokensEnCache = null);

/// <summary>Métricas de una corrida (micro-promedios sobre los casos).</summary>
public sealed record Metricas(
    int Casos, double Clasificacion, double Cita, double Cobertura, double DatosPrecision, double DatosRecall,
    double DatosF1, double DatosExactos, double ExclusionesPrecision, double ExclusionesRecall,
    double SenalesPrecision, double SenalesRecall, double RecuperacionDeLaCobertura,
    double RecuperacionDeExclusiones, double RespuestasLegibles, double LlamadasAFuncion, int PiiEnPrompts,
    double? LatenciaMediaMs, double? LatenciaP95Ms, int LlamadasAlModelo = 0, long? TokensDeEntrada = null,
    long? TokensDeSalida = null, double? LatenciaDelModeloMediaMs = null, double? LatenciaDelModeloP95Ms = null,
    long? TokensEnCache = null);

/// <summary>Una corrida completa con un contexto.</summary>
public sealed record Corrida(Contexto Contexto, Metricas Metricas, IReadOnlyList<ResultadoDeCaso> Casos);

/// <summary>Costo a precio de lista (USD por millón de tokens) de lo que reportó el servidor.</summary>
public sealed record CostoDeLista(
    decimal UsdPorMillonDeEntrada, decimal UsdPorMillonDeEntradaEnCache, decimal UsdPorMillonDeSalida, decimal TotalUsd);

/// <summary>El informe de un proveedor: la huella del conjunto y las tres corridas. Fecha, ajustes, costo y espera por
/// ritmo sólo para un LLM real (en la línea base serían ruido).</summary>
public sealed record Informe(
    string Proveedor, string Modelo, string HuellaDelConjunto, IReadOnlyList<Corrida> Corridas, string? Fecha = null,
    string? Ajustes = null, CostoDeLista? Costo = null, double? SegundosDeEsperaPorRitmo = null);

/// <summary>
/// Corre el conjunto con la composición REAL de Application (en memoria): alta del titular, emisión de la póliza,
/// aviso, y la propuesta por el mismo manejador que usa la API (seudonimización, asistente, validación). Un espía
/// alrededor del servicio de chat guarda todo lo que se le envió, para medir si llegó algún dato personal.
/// </summary>
/// <remarks>
/// Con un LLM real: el servicio pasa por el ritmo (sin ráfagas) y, si se da <paramref name="cache"/>, cada caso
/// terminado se guarda ahí para reanudar sin repetirlo si un límite diario corta la corrida.
/// </remarks>
public sealed class Evaluador(
    string carpetaEval, OpcionesDelAsistente opciones, IChatCompletionService? chat = null, string? cache = null,
    Func<IChatCompletionService, IChatCompletionService>? ritmo = null)
{
    private static readonly DateTimeOffset Ahora = new(2026, 6, 15, 17, 0, 0, TimeSpan.Zero);

    /// <summary>Segundos que el ritmo hizo esperar (si lo hubo).</summary>
    public double SegundosDeEspera => (_ritmo as ChatConRitmo)?.SegundosEsperados ?? 0;

    private IChatCompletionService? _ritmo;

    // La pareja del control barajado sale del conjunto completo, aunque se corra un subconjunto (--casos N).
    private IReadOnlyList<CasoDeEvaluacion> _todos = [];

    /// <summary>Corre los contextos pedidos (por omisión, los tres).</summary>
    public async Task<Informe> CorrerAsync(IReadOnlyList<Contexto>? contextos = null, int? limite = null,
        Action<string>? progreso = null, CancellationToken ct = default)
    {
        _todos = Conjunto.Casos(carpetaEval);
        var casos = _todos.Take(limite ?? int.MaxValue).ToList();
        var caratulas = Conjunto.Caratulas(carpetaEval);
        var espia = new ChatEspia(chat ?? Fabrica.Chat(opciones));
        _ritmo = (ritmo ?? (opciones.Proveedor == "groq"
            ? s => new ChatConRitmo(s, opciones.SolicitudesPorMinuto, opciones.TokensPorMinuto)
            : s => s))(espia);
        var guardados = LeerCache();
        var corridas = new List<Corrida>();
        foreach (var contexto in contextos ?? [Contexto.Normal, Contexto.Vacio, Contexto.Barajado])
        {
            var corrida = await CorrerAsync(contexto, casos, caratulas, espia, guardados, progreso, ct);
            // La latencia de la línea base no mide nada (no es un modelo) y haría variar el informe versionado.
            corridas.Add(opciones.Proveedor == "extractivo"
                ? corrida with { Metricas = corrida.Metricas with { LatenciaMediaMs = null, LatenciaP95Ms = null } }
                : corrida);
        }

        return new Informe(opciones.Proveedor, Ejecucion.NombreDelModelo(opciones), Huella(carpetaEval), corridas);
    }

    private async Task<Corrida> CorrerAsync(Contexto contexto, IReadOnlyList<CasoDeEvaluacion> casos,
        IReadOnlyDictionary<string, CaratulaDeCaso> caratulas, ChatEspia espia,
        Dictionary<string, ResultadoDeCaso> guardados, Action<string>? progreso, CancellationToken ct)
    {
        var bm25 = new RecuperadorBm25();
        var control = new RecuperadorDeControl(bm25, contexto);
        var servicios = new ServiceCollection().AgregarPeritoEnMemoria(new AlmacenEnMemoria(), new RelojFijo(Ahora));
        servicios.AddScoped<IAsistenteDeTriaje>(p =>
            new AsistenteDeTriaje(_ritmo!, control, p.GetRequiredService<IMediador>(), opciones));
        await using var proveedor = servicios.BuildServiceProvider();

        var resultados = new List<ResultadoDeCaso>();
        foreach (var (caso, i) in casos.Select((c, i) => (c, i)))
        {
            if (guardados.TryGetValue($"{contexto}/{caso.Id}", out var guardado))
            {
                resultados.Add(guardado);
                continue;
            }

            var caratula = caratulas[caso.Poliza];
            control.Sustituto = contexto == Contexto.Barajado ? Pareja(_todos, caratulas, i).Descripcion : null;
            espia.Reiniciar();
            var propuesta = await ProponerAsync(proveedor, caso, caratula, ct);
            var resultado = Calificar(caso, propuesta, espia, conLatencia: opciones.Proveedor != "extractivo");
            resultados.Add(resultado);
            Guardar(contexto, resultado);
            progreso?.Invoke($"{contexto} {caso.Id}: {propuesta.Tipo?.ToString() ?? "—"} {propuesta.Clausula ?? "—"} " +
                             $"({propuesta.LatenciaMs} ms)");
        }

        return new Corrida(contexto, Medir(casos, resultados), resultados);
    }

    private Dictionary<string, ResultadoDeCaso> LeerCache()
    {
        var guardados = new Dictionary<string, ResultadoDeCaso>(StringComparer.Ordinal);
        if (cache is null || !File.Exists(cache))
        {
            return guardados;
        }

        foreach (var linea in File.ReadLines(cache).Where(l => l.Length > 0))
        {
            var e = JsonSerializer.Deserialize<EntradaDeCache>(linea, Program.Json)!;
            guardados[$"{e.Contexto}/{e.Resultado.Id}"] = e.Resultado;
        }

        return guardados;
    }

    private void Guardar(Contexto contexto, ResultadoDeCaso resultado)
    {
        if (cache is not null)
        {
            File.AppendAllText(cache, JsonSerializer.Serialize(new EntradaDeCache(contexto, resultado),
                Program.JsonDeLinea) + "\n");
        }
    }

    private sealed record EntradaDeCache(Contexto Contexto, ResultadoDeCaso Resultado);

    private static async Task<PropuestaDeTriaje> ProponerAsync(ServiceProvider proveedor, CasoDeEvaluacion caso,
        CaratulaDeCaso caratula, CancellationToken ct)
    {
        await using var alcance = proveedor.CreateAsyncScope();
        var mediador = alcance.ServiceProvider.GetRequiredService<IMediador>();
        var t = caso.Titular;
        var titular = await mediador.EnviarAsync(new RegistrarAsegurado(t.Nombre, t.Rfc, t.Correo, t.Telefono, null), ct);
        var folio = $"EVAL-{caso.Id}";
        Exigir(await mediador.EnviarAsync(new EmitirPoliza(folio, caratula.Ramo, titular.Valor,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31), caratula.Coberturas), ct));
        var siniestro = await mediador.EnviarAsync(new AvisarSiniestro(folio, new DateOnly(2026, 6, 14),
            caso.Descripcion, null, null), ct);
        Exigir(siniestro);
        var propuesta = await mediador.EnviarAsync(new ProponerTriaje(siniestro.Valor, "evaluacion"), ct);
        Exigir(propuesta);
        return propuesta.Valor;
    }

    private static void Exigir(Domain.Comun.Resultado r)
    {
        if (!r.EsExito)
        {
            throw new InvalidOperationException($"El caso no se pudo preparar: {r.Error!.Codigo} {r.Error.Mensaje}");
        }
    }

    /// <summary>El caso siguiente (en orden circular) del mismo ramo y con otra cláusula esperada.</summary>
    public static CasoDeEvaluacion Pareja(IReadOnlyList<CasoDeEvaluacion> casos,
        IReadOnlyDictionary<string, CaratulaDeCaso> caratulas, int i)
    {
        var ramo = caratulas[casos[i].Poliza].Ramo;
        for (var k = 1; k < casos.Count; k++)
        {
            var otro = casos[(i + k) % casos.Count];
            if (caratulas[otro.Poliza].Ramo == ramo && otro.Esperado.Clausula != casos[i].Esperado.Clausula)
            {
                return otro;
            }
        }

        throw new InvalidOperationException($"Sin pareja para {casos[i].Id}.");
    }

    private static ResultadoDeCaso Calificar(CasoDeEvaluacion caso, PropuestaDeTriaje p, ChatEspia espia,
        bool conLatencia)
    {
        var e = caso.Esperado;
        var datos = p.DatosFaltantes.Select(d => d.ToString()).ToList();
        var enviado = espia.TextoEnviado();
        var t = caso.Titular;
        var pii = caso.Pii.Concat([t.Nombre, t.Correo, t.Telefono, t.Rfc, Digitos(t.Telefono)])
            .Count(v => enviado.Contains(v, StringComparison.OrdinalIgnoreCase));
        return new ResultadoDeCaso(caso.Id, $"{e.Tipo} {e.Clausula}", p.Tipo?.ToString(), p.Clausula, datos,
            p.Exclusiones, p.Senales.Select(s => s.ToString()).ToList(), p.ClausulasRecuperadas, espia.LlamoFuncion,
            pii, conLatencia ? p.LatenciaMs : null, p.Observaciones, p.Tipo?.ToString() == e.Tipo, p.Clausula == e.Clausula,
            datos.Order().SequenceEqual(e.DatosFaltantes.Order()), espia.Llamadas, espia.TokensDeEntrada,
            espia.TokensDeSalida, conLatencia ? espia.Latencias.ToList() : null, espia.TokensEnCache);
    }

    private static Metricas Medir(IReadOnlyList<CasoDeEvaluacion> casos, IReadOnlyList<ResultadoDeCaso> r)
    {
        var n = casos.Count;
        var pares = casos.Zip(r).ToList();
        double Parte(Func<CasoDeEvaluacion, ResultadoDeCaso, bool> f) =>
            Math.Round(pares.Count(x => f(x.First, x.Second)) / (double)n, 4);

        var (dp, dr, df) = Micro(pares.Select(x => (x.First.Esperado.DatosFaltantes, x.Second.DatosFaltantes)));
        var (ep, er, _) = Micro(pares.Select(x => (x.First.Esperado.Exclusiones, x.Second.Exclusiones)));
        var (sp, sr, _) = Micro(pares.Select(x => (x.First.Esperado.Senales, x.Second.Senales)));
        var exclusionesEsperadas = pares.Sum(x => x.First.Esperado.Exclusiones.Count);
        var exclusionesRecuperadas = pares.Sum(x => x.First.Esperado.Exclusiones.Count(x.Second.Recuperadas.Contains));
        var latencias = r.Select(x => (double)(x.LatenciaMs ?? 0)).Order().ToList();
        return new Metricas(n,
            Parte((_, x) => x.TipoCorrecto),
            Parte((_, x) => x.CitaCorrecta),
            Parte((c, x) => x.Tipo is { } tipo && Catalogo.De(Enum.Parse<TipoDeSiniestro>(tipo)).Cobertura.ToString() ==
                c.Esperado.Cobertura),
            dp, dr, df,
            Parte((_, x) => x.DatosExactos),
            ep, er, sp, sr,
            Parte((c, x) => x.Recuperadas.Contains(c.Esperado.Clausula)),
            exclusionesEsperadas == 0 ? 1 : Math.Round(exclusionesRecuperadas / (double)exclusionesEsperadas, 4),
            Parte((_, x) => !x.Observaciones.Any(o => o.Contains("JSON", StringComparison.Ordinal))),
            Parte((_, x) => x.LlamoFuncion),
            r.Sum(x => x.PiiEnPrompt),
            Math.Round(latencias.Average(), 1),
            latencias[(int)Math.Ceiling(0.95 * latencias.Count) - 1],
            r.Sum(x => x.LlamadasAlModelo),
            r.Any(x => x.TokensDeEntrada is not null) ? r.Sum(x => (long)(x.TokensDeEntrada ?? 0)) : null,
            r.Any(x => x.TokensDeSalida is not null) ? r.Sum(x => (long)(x.TokensDeSalida ?? 0)) : null,
            Percentil(r.SelectMany(x => x.LatenciasDelModeloMs ?? []).ToList(), media: true),
            Percentil(r.SelectMany(x => x.LatenciasDelModeloMs ?? []).ToList(), media: false),
            r.Any(x => x.TokensEnCache is not null) ? r.Sum(x => (long)(x.TokensEnCache ?? 0)) : null);
    }

    private static double? Percentil(List<long> valores, bool media)
    {
        if (valores.Count == 0)
        {
            return null;
        }

        valores.Sort();
        return media ? Math.Round(valores.Average(), 1) : valores[(int)Math.Ceiling(0.95 * valores.Count) - 1];
    }

    /// <summary>Precisión, exhaustividad y F1 micro sobre pares (esperado, propuesto); sin predicciones la precisión
    /// es 1 y sin esperados la exhaustividad es 1.</summary>
    public static (double Precision, double Exhaustividad, double F1) Micro(
        IEnumerable<(IReadOnlyList<string> Esperado, IReadOnlyList<string> Propuesto)> pares)
    {
        int vp = 0, fp = 0, fn = 0;
        foreach (var (esperado, propuesto) in pares)
        {
            vp += propuesto.Intersect(esperado).Count();
            fp += propuesto.Except(esperado).Count();
            fn += esperado.Except(propuesto).Count();
        }

        var p = vp + fp == 0 ? 1 : vp / (double)(vp + fp);
        var r = vp + fn == 0 ? 1 : vp / (double)(vp + fn);
        var f1 = p + r == 0 ? 0 : 2 * p * r / (p + r);
        return (Math.Round(p, 4), Math.Round(r, 4), Math.Round(f1, 4));
    }

    /// <summary>sha256 del conjunto con fines de línea normalizados: el informe dice contra qué versión se midió.</summary>
    public static string Huella(string carpetaEval)
    {
        var texto = File.ReadAllText(Path.Join(carpetaEval, "siniestros.jsonl")).ReplaceLineEndings("\n");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..16];
    }

    private static string Digitos(string s) => new(s.Where(char.IsAsciiDigit).ToArray());

    /// <summary>Recuperador de los controles negativos.</summary>
    private sealed class RecuperadorDeControl(IRecuperadorDeClausulas real, Contexto contexto) : IRecuperadorDeClausulas
    {
        public string? Sustituto { get; set; }

        public ContextoRecuperado Buscar(string texto, Ramo ramo) => contexto switch
        {
            Contexto.Normal => real.Buscar(texto, ramo),
            Contexto.Vacio => ContextoRecuperado.Vacio,
            _ => real.Buscar(Sustituto ?? throw new InvalidOperationException("sin sustituto"), ramo),
        };
    }

    /// <summary>Guarda todo lo que se envía al modelo y si pidió la función.</summary>
    private sealed class ChatEspia(IChatCompletionService interno) : IChatCompletionService
    {
        private readonly List<string> _enviado = [];
        private readonly List<long> _latencias = [];

        public bool LlamoFuncion { get; private set; }

        public int Llamadas { get; private set; }

        public int? TokensDeEntrada { get; private set; }

        public int? TokensDeSalida { get; private set; }

        public int? TokensEnCache { get; private set; }

        public IReadOnlyList<long> Latencias => _latencias;

        public IReadOnlyDictionary<string, object?> Attributes => interno.Attributes;

        public void Reiniciar()
        {
            _enviado.Clear();
            _latencias.Clear();
            LlamoFuncion = false;
            Llamadas = 0;
            TokensDeEntrada = TokensDeSalida = TokensEnCache = null;
        }

        public string TextoEnviado() => string.Join("\n", _enviado);

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            foreach (var m in chatHistory)
            {
                _enviado.Add(m.Content ?? string.Empty);
                _enviado.AddRange(m.Items.OfType<FunctionResultContent>().Select(f => f.Result?.ToString() ?? ""));
            }

            var cronometro = System.Diagnostics.Stopwatch.StartNew();
            var respuesta = await interno.GetChatMessageContentsAsync(chatHistory, executionSettings, kernel,
                cancellationToken);
            _latencias.Add(cronometro.ElapsedMilliseconds);
            Llamadas++;
            LlamoFuncion |= respuesta.Any(m => m.Items.OfType<FunctionCallContent>().Any());
            if (respuesta.Select(Uso.De).FirstOrDefault(u => u is not null) is { } uso)
            {
                TokensDeEntrada = (TokensDeEntrada ?? 0) + uso.Entrada;
                TokensEnCache = (TokensEnCache ?? 0) + uso.EnCache;
                TokensDeSalida = (TokensDeSalida ?? 0) + uso.Salida;
            }

            return respuesta;
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var m in await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel,
                         cancellationToken))
            {
                yield return new StreamingChatMessageContent(m.Role, m.Content);
            }
        }
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Perito.Asistente;
using Perito.Asistente.Proveedores;

namespace Perito.Evaluacion;

/// <summary>
/// perito-eval [--proveedor extractivo|ollama|groq] [--modelo M] [--endpoint URL] [--eval DIR] [--salida DIR]
/// [--casos N] [--contextos normal,vacio,barajado] [--compuerta eval/umbrales.json]
/// Con un LLM real: --fecha AAAA-MM-DD [--archivo-de-llave RUTA] [--esfuerzo low] [--rpm 20] [--tpm 7000]
/// [--usd-entrada 0.15 --usd-cache 0.075 --usd-salida 0.60] [--cache RUTA.jsonl]
/// </summary>
public static class Program
{
    /// <summary>Opciones de JSON del informe.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Las mismas opciones en una sola línea (caché de casos).</summary>
    public static readonly JsonSerializerOptions JsonDeLinea = new(Json) { WriteIndented = false };

    /// <summary>Punto de entrada.</summary>
    public static Task<int> Main(string[] args) => EjecutarAsync(args, Console.Out, Console.Error);

    /// <summary>
    /// La corrida completa. La llave de Groq se lee del archivo y no se imprime nunca: la salida, el informe y la caché
    /// sólo llevan modelo, métricas, tokens y latencias (lo verifica una prueba con un servidor falso).
    /// </summary>
    public static async Task<int> EjecutarAsync(string[] args, TextWriter consola, TextWriter errores,
        HttpClient? transporte = null)
    {
        var a = Argumentos(args);
        var eval = a.GetValueOrDefault("eval") ?? Conjunto.Buscar(Directory.GetCurrentDirectory());
        var proveedor = a.GetValueOrDefault("proveedor") ?? "extractivo";
        var groq = proveedor == "groq";
        var opciones = new OpcionesDelAsistente
        {
            Proveedor = proveedor,
            Modelo = a.GetValueOrDefault("modelo") ?? (groq ? ModeloDeGroq : "qwen2.5:1.5b"),
            Endpoint = a.GetValueOrDefault("endpoint") ?? (groq ? Fabrica.EndpointDeGroq : "http://127.0.0.1:11434"),
            Llave = groq ? SecretoApi.LeerDeArchivo(a.GetValueOrDefault("archivo-de-llave") ?? ArchivoDeLlave,
                "GROQ_API_KEY") : null,
            EsfuerzoDeRazonamiento = a.GetValueOrDefault("esfuerzo") ?? "low",
            SolicitudesPorMinuto = Entero(a, "rpm") ?? 20,
            TokensPorMinuto = Entero(a, "tpm") ?? 7000,
            TiempoMaximo = proveedor == "extractivo" ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(15),
            Transporte = transporte,
        };
        var fecha = a.GetValueOrDefault("fecha");
        if (proveedor != "extractivo" && fecha is null)
        {
            await errores.WriteLineAsync("Indique --fecha AAAA-MM-DD: el informe de un LLM real lleva la fecha de la corrida.");
            return 2;
        }

        var contextos = a.TryGetValue("contextos", out var c)
            ? c.Split(',').Select(x => Enum.Parse<Contexto>(x, ignoreCase: true)).ToList()
            : null;
        var salida = a.GetValueOrDefault("salida") ?? Path.Join(eval, "informe");
        var nombre = proveedor switch
        {
            "extractivo" or "groq" => proveedor,
            _ => $"{proveedor}-{opciones.Modelo.Replace(':', '-').Replace('/', '-')}",
        };
        var cache = a.GetValueOrDefault("cache");
        if (cache is null && proveedor != "extractivo")
        {
            Directory.CreateDirectory(Path.Join(eval, "corridas"));
            cache = Path.Join(eval, "corridas", nombre + "-casos.jsonl");
        }

        var evaluador = new Evaluador(eval, opciones, cache: cache);
        Informe informe;
        try
        {
            informe = await evaluador.CorrerAsync(contextos, Entero(a, "casos"), x => errores.WriteLine(x));
        }
        catch (Microsoft.SemanticKernel.HttpOperationException e)
        {
            // Sólo el estado HTTP: el cuerpo del error puede traer datos de la cuenta.
            await errores.WriteLineAsync($"corrida interrumpida por el proveedor (HTTP {(int?)e.StatusCode}); los " +
                                         $"casos terminados quedaron en {cache}: reanude con el mismo comando.");
            return 3;
        }

        if (proveedor != "extractivo")
        {
            informe = informe with
            {
                Fecha = fecha,
                Ajustes = $"temperature=0 · reasoning_effort={opciones.EsfuerzoDeRazonamiento} · " +
                          $"max_tokens={opciones.TokensMaximosDeSalida} · ritmo {opciones.SolicitudesPorMinuto} RPM y " +
                          $"{opciones.TokensPorMinuto} TPM · reintentos con retry-after (tope 90 s)",
                Costo = Costo(informe, Decimal(a, "usd-entrada"), Decimal(a, "usd-cache") ?? Decimal(a, "usd-entrada"),
                    Decimal(a, "usd-salida")),
                SegundosDeEsperaPorRitmo = Math.Round(evaluador.SegundosDeEspera, 1),
            };
        }

        Directory.CreateDirectory(salida);
        var lineaBase = proveedor == "extractivo" ? null : LeerInforme(Path.Join(eval, "informe", "extractivo.json"));
        await File.WriteAllTextAsync(Path.Join(salida, nombre + ".json"), JsonSerializer.Serialize(informe, Json) + "\n");
        await File.WriteAllTextAsync(Path.Join(salida, nombre + ".md"), Markdown(informe, lineaBase));
        await consola.WriteLineAsync(Resumen(informe));

        if (a.TryGetValue("compuerta", out var umbrales))
        {
            var fallas = Compuerta.Revisar(informe, umbrales);
            foreach (var f in fallas)
            {
                await consola.WriteLineAsync("COMPUERTA: " + f);
            }

            await consola.WriteLineAsync(fallas.Count == 0 ? "compuerta: en verde" : $"compuerta: {fallas.Count} falla(s)");
            return fallas.Count == 0 ? 0 : 1;
        }

        return 0;
    }

    /// <summary>Modelo de Groq por omisión (docs/adr/0009).</summary>
    public const string ModeloDeGroq = "openai/gpt-oss-120b";

    /// <summary>Dónde vive la llave de Groq (permisos 600).</summary>
    public static readonly string ArchivoDeLlave = Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "perito", "groq.env");

    private static CostoDeLista? Costo(Informe informe, decimal? entrada, decimal? enCache, decimal? salida)
    {
        if (entrada is null || enCache is null || salida is null)
        {
            return null;
        }

        var tokensEntrada = informe.Corridas.Sum(c => c.Metricas.TokensDeEntrada ?? 0);
        var tokensEnCache = informe.Corridas.Sum(c => c.Metricas.TokensEnCache ?? 0);
        var tokensSalida = informe.Corridas.Sum(c => c.Metricas.TokensDeSalida ?? 0);
        return new CostoDeLista(entrada.Value, enCache.Value, salida.Value, Math.Round(
            ((tokensEntrada - tokensEnCache) * entrada.Value + tokensEnCache * enCache.Value +
             tokensSalida * salida.Value) / 1_000_000m, 4));
    }

    private static Informe? LeerInforme(string ruta) =>
        File.Exists(ruta) ? JsonSerializer.Deserialize<Informe>(File.ReadAllText(ruta), Json) : null;

    private static int? Entero(Dictionary<string, string> a, string llave) =>
        a.TryGetValue(llave, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : null;

    private static decimal? Decimal(Dictionary<string, string> a, string llave) =>
        a.TryGetValue(llave, out var v) ? decimal.Parse(v, CultureInfo.InvariantCulture) : null;

    private static Dictionary<string, string> Argumentos(string[] args)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Argumento inesperado: {args[i]}");
            }

            d[args[i][2..]] = args[i + 1];
        }

        return d;
    }

    /// <summary>Tabla corta para la consola.</summary>
    public static string Resumen(Informe informe)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"{informe.Proveedor} · {informe.Modelo} · conjunto {informe.HuellaDelConjunto}");
        foreach (var c in informe.Corridas)
        {
            var m = c.Metricas;
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  {c.Contexto,-9} clasificación {m.Clasificacion:0.00} · cita {m.Cita:0.00} · datos F1 {m.DatosF1:0.00} · " +
                $"recuperación {m.RecuperacionDeLaCobertura:0.00} · PII en prompts {m.PiiEnPrompts}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// El informe en Markdown: resumen por contexto, la comparación contra la línea base si se da, el uso (llamadas,
    /// tokens, costo y latencia del modelo) y el detalle del contexto normal.
    /// </summary>
    public static string Markdown(Informe informe, Informe? lineaBase = null)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine(inv, $"# Evaluación del asistente · {informe.Proveedor} ({informe.Modelo})");
        sb.AppendLine();
        sb.AppendLine(inv, $"Conjunto `eval/siniestros.jsonl` (huella `{informe.HuellaDelConjunto}`), " +
                           $"{informe.Corridas[0].Metricas.Casos} casos. Generado por `perito-eval`; no se edita a mano.");
        sb.AppendLine();
        if (informe.Fecha is not null)
        {
            sb.AppendLine(inv, $"- Corrida: {informe.Fecha} · {informe.Ajustes}");
            sb.AppendLine(inv, $"- Espera por el ritmo (sin ráfagas): {informe.SegundosDeEsperaPorRitmo:0} s");
            sb.AppendLine("- Mismo RAG, mismo prompt, misma función de la póliza y mismas validaciones que la línea base; " +
                          "sólo cambia el servicio de chat.");
            sb.AppendLine();
        }

        sb.Append("| Métrica |");
        foreach (var c in informe.Corridas)
        {
            sb.Append(inv, $" {c.Contexto} |");
        }

        sb.AppendLine().Append("|---|").AppendLine(string.Concat(informe.Corridas.Select(_ => "---:|")));
        (string, Func<Metricas, string>)[] filas =
        [
            ("Clasificación (tipo exacto)", m => m.Clasificacion.ToString("0.00", inv)),
            ("Cita correcta (cláusula exacta)", m => m.Cita.ToString("0.00", inv)),
            ("Cobertura correcta", m => m.Cobertura.ToString("0.00", inv)),
            ("Datos faltantes: precisión", m => m.DatosPrecision.ToString("0.00", inv)),
            ("Datos faltantes: exhaustividad", m => m.DatosRecall.ToString("0.00", inv)),
            ("Datos faltantes: F1", m => m.DatosF1.ToString("0.00", inv)),
            ("Datos faltantes: conjunto exacto", m => m.DatosExactos.ToString("0.00", inv)),
            ("Exclusiones: precisión", m => m.ExclusionesPrecision.ToString("0.00", inv)),
            ("Exclusiones: exhaustividad", m => m.ExclusionesRecall.ToString("0.00", inv)),
            ("Señales: precisión", m => m.SenalesPrecision.ToString("0.00", inv)),
            ("Señales: exhaustividad", m => m.SenalesRecall.ToString("0.00", inv)),
            ("Recuperación de la cláusula esperada (top 4)", m => m.RecuperacionDeLaCobertura.ToString("0.00", inv)),
            ("Recuperación de exclusiones esperadas (top 3)", m => m.RecuperacionDeExclusiones.ToString("0.00", inv)),
            ("Respuestas JSON legibles", m => m.RespuestasLegibles.ToString("0.00", inv)),
            ("Llamó a la función de la póliza", m => m.LlamadasAFuncion.ToString("0.00", inv)),
            ("Datos personales en los prompts", m => m.PiiEnPrompts.ToString(inv)),
            ("Latencia media por propuesta (ms, con la espera del ritmo)", m => m.LatenciaMediaMs?.ToString("0", inv) ?? "no aplica"),
            ("Latencia p95 por propuesta (ms)", m => m.LatenciaP95Ms?.ToString("0", inv) ?? "no aplica"),
            ("Llamadas al modelo", m => m.LlamadasAlModelo.ToString(inv)),
            ("Tokens de entrada", m => m.TokensDeEntrada?.ToString("N0", inv) ?? "no aplica"),
            ("Tokens de entrada servidos de la caché de prompts", m => m.TokensEnCache?.ToString("N0", inv) ?? "no aplica"),
            ("Tokens de salida (incluye razonamiento)", m => m.TokensDeSalida?.ToString("N0", inv) ?? "no aplica"),
            ("Latencia media por llamada al modelo (ms)", m => m.LatenciaDelModeloMediaMs?.ToString("0", inv) ?? "no aplica"),
            ("Latencia p95 por llamada al modelo (ms)", m => m.LatenciaDelModeloP95Ms?.ToString("0", inv) ?? "no aplica"),
        ];
        foreach (var (nombre, valor) in filas)
        {
            sb.Append(inv, $"| {nombre} |");
            foreach (var c in informe.Corridas)
            {
                sb.Append(inv, $" {valor(c.Metricas)} |");
            }

            sb.AppendLine();
        }

        if (informe.Costo is { } costo)
        {
            sb.AppendLine().AppendLine(string.Format(inv,
                "Costo a precio de lista (USD por millón: {0} de entrada, {1} de entrada en caché, {2} de salida): " +
                "**USD {3:0.0000}** por las corridas de este informe (en la capa gratuita no se cobra).",
                costo.UsdPorMillonDeEntrada, costo.UsdPorMillonDeEntradaEnCache, costo.UsdPorMillonDeSalida,
                costo.TotalUsd));
        }

        if (lineaBase is not null)
        {
            Comparacion(sb, informe, lineaBase);
        }

        var normal = informe.Corridas.FirstOrDefault(c => c.Contexto == Contexto.Normal);
        if (normal is not null)
        {
            sb.AppendLine().AppendLine("## Detalle (contexto normal)").AppendLine();
            sb.AppendLine("| Caso | Esperado | Propuesto | Tipo | Cita | Datos faltantes | Exclusiones | Observaciones |");
            sb.AppendLine("|---|---|---|:-:|:-:|---|---|---|");
            foreach (var r in normal.Casos)
            {
                sb.AppendLine(inv,
                    $"| {r.Id} | {r.Esperado} | {r.Tipo ?? "—"} {r.Clausula ?? "—"} | {(r.TipoCorrecto ? "✓" : "✗")} | " +
                    $"{(r.CitaCorrecta ? "✓" : "✗")} | {string.Join(", ", r.DatosFaltantes)}{(r.DatosExactos ? "" : " ✗")} | " +
                    $"{string.Join(", ", r.Exclusiones)} | {string.Join("; ", r.Observaciones).Replace("|", "/")} |");
            }
        }

        return sb.ToString();
    }

    private static void Comparacion(StringBuilder sb, Informe llm, Informe lineaBase)
    {
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine().AppendLine(inv, $"## {llm.Modelo} contra la línea base determinista ({lineaBase.Modelo})")
            .AppendLine();
        sb.AppendLine("La línea base NO es un LLM: elige por coincidencia ponderada de palabras. El LLM recibe exactamente " +
                      "el mismo contexto.").AppendLine();
        sb.Append("| Métrica |");
        var contextos = llm.Corridas.Select(c => c.Contexto).ToList();
        foreach (var c in contextos)
        {
            sb.Append(inv, $" LLM {c} | Base {c} |");
        }

        sb.AppendLine().Append("|---|").AppendLine(string.Concat(contextos.Select(_ => "---:|---:|")));
        (string, Func<Metricas, double>)[] filas =
        [
            ("Clasificación", m => m.Clasificacion), ("Cita", m => m.Cita), ("Datos faltantes F1", m => m.DatosF1),
            ("Exclusiones: exhaustividad", m => m.ExclusionesRecall), ("Exclusiones: precisión", m => m.ExclusionesPrecision),
            ("Señales: exhaustividad", m => m.SenalesRecall), ("Respuestas JSON legibles", m => m.RespuestasLegibles),
            ("Llamó a la función", m => m.LlamadasAFuncion), ("Datos personales en los prompts", m => m.PiiEnPrompts),
        ];
        foreach (var (nombre, valor) in filas)
        {
            sb.Append(inv, $"| {nombre} |");
            foreach (var c in contextos)
            {
                var a = llm.Corridas.Single(x => x.Contexto == c).Metricas;
                var b = lineaBase.Corridas.SingleOrDefault(x => x.Contexto == c)?.Metricas;
                sb.Append(inv, $" {valor(a):0.00} | {(b is null ? "—" : valor(b).ToString("0.00", inv))} |");
            }

            sb.AppendLine();
        }
    }
}

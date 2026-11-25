using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Perito.Asistente;

namespace Perito.Evaluacion;

/// <summary>
/// perito-eval [--proveedor extractivo|ollama] [--modelo M] [--endpoint URL] [--eval DIR] [--salida DIR]
/// [--casos N] [--contextos normal,vacio,barajado] [--compuerta eval/umbrales.json]
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

    /// <summary>Punto de entrada.</summary>
    public static async Task<int> Main(string[] args)
    {
        var a = Argumentos(args);
        var eval = a.GetValueOrDefault("eval") ?? Conjunto.Buscar(Directory.GetCurrentDirectory());
        var opciones = new OpcionesDelAsistente
        {
            Proveedor = a.GetValueOrDefault("proveedor") ?? "extractivo",
            Modelo = a.GetValueOrDefault("modelo") ?? "qwen2.5:1.5b",
            Endpoint = a.GetValueOrDefault("endpoint") ?? "http://127.0.0.1:11434",
        };
        var contextos = a.TryGetValue("contextos", out var c)
            ? c.Split(',').Select(x => Enum.Parse<Contexto>(x, ignoreCase: true)).ToList()
            : null;
        int? limite = a.TryGetValue("casos", out var n) ? int.Parse(n, CultureInfo.InvariantCulture) : null;

        var informe = await new Evaluador(eval, opciones).CorrerAsync(contextos, limite, Console.Error.WriteLine);
        var salida = a.GetValueOrDefault("salida") ?? Path.Join(eval, "informe");
        Directory.CreateDirectory(salida);
        var nombre = opciones.Proveedor == "extractivo"
            ? "extractivo"
            : $"{opciones.Proveedor}-{opciones.Modelo.Replace(':', '-').Replace('/', '-')}";
        await File.WriteAllTextAsync(Path.Join(salida, nombre + ".json"), JsonSerializer.Serialize(informe, Json) + "\n");
        await File.WriteAllTextAsync(Path.Join(salida, nombre + ".md"), Markdown(informe));
        Console.WriteLine(Resumen(informe));

        if (a.TryGetValue("compuerta", out var umbrales))
        {
            var fallas = Compuerta.Revisar(informe, umbrales);
            foreach (var f in fallas)
            {
                Console.WriteLine("COMPUERTA: " + f);
            }

            Console.WriteLine(fallas.Count == 0 ? "compuerta: en verde" : $"compuerta: {fallas.Count} falla(s)");
            return fallas.Count == 0 ? 0 : 1;
        }

        return 0;
    }

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

    /// <summary>El informe en Markdown: resumen por contexto y el detalle del contexto normal.</summary>
    public static string Markdown(Informe informe)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine(inv, $"# Evaluación del asistente · {informe.Proveedor} ({informe.Modelo})");
        sb.AppendLine();
        sb.AppendLine(inv, $"Conjunto `eval/siniestros.jsonl` (huella `{informe.HuellaDelConjunto}`), " +
                           $"{informe.Corridas[0].Metricas.Casos} casos. Generado por `perito-eval`; no se edita a mano.");
        sb.AppendLine();
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
            ("Latencia media (ms)", m => m.LatenciaMediaMs?.ToString("0", inv) ?? "no aplica"),
            ("Latencia p95 (ms)", m => m.LatenciaP95Ms?.ToString("0", inv) ?? "no aplica"),
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
}

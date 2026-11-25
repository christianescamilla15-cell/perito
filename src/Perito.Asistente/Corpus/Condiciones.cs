using System.Reflection;
using System.Text.RegularExpressions;

namespace Perito.Asistente.Corpus;

/// <summary>Una cláusula citable de las condiciones generales con sus metadatos.</summary>
public sealed record Fragmento(
    string Id, string Titulo, string Ramo, string? Cobertura, string? Tipo, string? Exclusion,
    IReadOnlyList<string> Aplica, IReadOnlyList<string> Datos, string Texto)
{
    /// <summary>Cláusula de cobertura (ampara un tipo de siniestro).</summary>
    public bool EsCobertura => Tipo is not null;

    /// <summary>Cláusula de exclusión.</summary>
    public bool EsExclusion => Exclusion is not null;
}

/// <summary>Lee las condiciones generales (recurso incrustado desde docs/condiciones) en fragmentos.</summary>
public static partial class LectorDeCondiciones
{
    private static readonly Lazy<IReadOnlyList<Fragmento>> Todos = new(() => Leer(Documento()));

    /// <summary>Los fragmentos del documento incrustado.</summary>
    public static IReadOnlyList<Fragmento> Fragmentos => Todos.Value;

    /// <summary>El documento incrustado.</summary>
    public static string Documento()
    {
        using var flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream("condiciones-generales.md")
            ?? throw new InvalidOperationException("Falta el recurso condiciones-generales.md.");
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd();
    }

    /// <summary>Parte un documento con el formato de docs/condiciones en fragmentos.</summary>
    public static IReadOnlyList<Fragmento> Leer(string documento)
    {
        var encabezados = Encabezado().Matches(documento.ReplaceLineEndings("\n"));
        var fragmentos = new List<Fragmento>();
        for (var i = 0; i < encabezados.Count; i++)
        {
            var m = encabezados[i];
            var fin = i + 1 < encabezados.Count ? encabezados[i + 1].Index : documento.Length;
            var cuerpo = documento.ReplaceLineEndings("\n")[(m.Index + m.Length)..fin];
            var corte = cuerpo.IndexOf("\n## ", StringComparison.Ordinal);
            if (corte >= 0)
            {
                cuerpo = cuerpo[..corte];
            }

            var meta = m.Groups["meta"].Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(par => par.Split(':', 2, StringSplitOptions.TrimEntries))
                .ToDictionary(par => par[0], par => par[1], StringComparer.Ordinal);
            fragmentos.Add(new Fragmento(m.Groups["id"].Value, m.Groups["titulo"].Value.Trim(), meta["ramo"],
                meta.GetValueOrDefault("cobertura"), meta.GetValueOrDefault("tipo"), meta.GetValueOrDefault("exclusion"),
                Lista(meta.GetValueOrDefault("aplica")), Lista(meta.GetValueOrDefault("datos")),
                string.Join(' ', cuerpo.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))));
        }

        return fragmentos;
    }

    private static string[] Lista(string? valor) =>
        valor?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

    [GeneratedRegex(@"^### (?<id>[A-Z]{3}-[0-9.]+(?:-[a-z])?) · (?<titulo>.+)\n<!-- (?<meta>.+) -->$", RegexOptions.Multiline)]
    private static partial Regex Encabezado();
}

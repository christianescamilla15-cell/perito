using System.Text.Json;
using System.Text.Json.Serialization;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;

namespace Perito.Evaluacion;

/// <summary>El titular ficticio de un caso.</summary>
public sealed record TitularDeCaso(string Nombre, string Correo, string Telefono, string Rfc);

/// <summary>Lo que se espera del asistente en un caso (etiquetado antes de escribir el modelo; eval/README.md).</summary>
public sealed record Esperado(
    string Tipo, string Cobertura, string Clausula,
    [property: JsonPropertyName("datos_faltantes")] IReadOnlyList<string> DatosFaltantes,
    IReadOnlyList<string> Exclusiones, IReadOnlyList<string> Senales);

/// <summary>Un siniestro sintético del conjunto.</summary>
public sealed record CasoDeEvaluacion(
    string Id, string Poliza, TitularDeCaso Titular, string Descripcion, IReadOnlyList<string> Pii, Esperado Esperado,
    string? Nota);

/// <summary>Una carátula del conjunto.</summary>
public sealed record CaratulaDeCaso(Ramo Ramo, IReadOnlyList<CoberturaContratada> Coberturas);

/// <summary>Lee eval/siniestros.jsonl y eval/polizas.json.</summary>
public static class Conjunto
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Los casos, en el orden del archivo.</summary>
    public static IReadOnlyList<CasoDeEvaluacion> Casos(string carpeta) =>
        File.ReadLines(Path.Join(carpeta, "siniestros.jsonl"))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<CasoDeEvaluacion>(l, Json)
                         ?? throw new InvalidDataException("caso vacío"))
            .ToList();

    /// <summary>Las carátulas por nombre.</summary>
    public static IReadOnlyDictionary<string, CaratulaDeCaso> Caratulas(string carpeta)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Join(carpeta, "polizas.json")));
        var caratulas = new Dictionary<string, CaratulaDeCaso>(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('_')))
        {
            var ramo = Enum.Parse<Ramo>(p.Value.GetProperty("ramo").GetString()!);
            var coberturas = p.Value.GetProperty("coberturas").EnumerateArray().Select(c =>
                new CoberturaContratada(
                    Enum.Parse<CodigoCobertura>(c.GetProperty("cobertura").GetString()!),
                    c.GetProperty("suma").GetDecimal(),
                    new Deducible(Enum.Parse<TipoDeDeducible>(c.GetProperty("deducible").GetProperty("tipo").GetString()!),
                        c.GetProperty("deducible").GetProperty("valor").GetDecimal()),
                    c.GetProperty("coaseguro").GetDecimal())).ToList();
            caratulas[p.Name] = new CaratulaDeCaso(ramo, coberturas);
        }

        return caratulas;
    }

    /// <summary>La carpeta eval/ del repositorio, buscándola hacia arriba desde un directorio.</summary>
    public static string Buscar(string desde)
    {
        var dir = new DirectoryInfo(desde);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "eval", "siniestros.jsonl")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? throw new DirectoryNotFoundException("No se encontró eval/siniestros.jsonl hacia arriba de " + desde)
            : Path.Join(dir.FullName, "eval");
    }
}

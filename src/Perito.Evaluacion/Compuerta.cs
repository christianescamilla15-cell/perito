using System.Globalization;
using System.Text.Json;

namespace Perito.Evaluacion;

/// <summary>
/// La compuerta de eval/umbrales.json: por contexto, «metrica_min» y «metrica_max» sobre las métricas del informe (en
/// snake_case), y «caida_minima_de_cita»: cuánto debe caer la cita con el control negativo respecto del contexto normal.
/// </summary>
public static class Compuerta
{
    /// <summary>Las fallas de la compuerta (vacía si pasa).</summary>
    public static IReadOnlyList<string> Revisar(Informe informe, string rutaUmbrales)
    {
        using var umbrales = JsonDocument.Parse(File.ReadAllText(rutaUmbrales));
        var fallas = new List<string>();
        var porContexto = informe.Corridas.ToDictionary(
            c => c.Contexto.ToString().ToLowerInvariant(),
            c => JsonSerializer.SerializeToElement(c.Metricas, Program.Json));
        foreach (var seccion in umbrales.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object))
        {
            if (!porContexto.TryGetValue(seccion.Name, out var metricas))
            {
                fallas.Add($"el informe no tiene el contexto {seccion.Name}");
                continue;
            }

            foreach (var regla in seccion.Value.EnumerateObject())
            {
                var minimo = regla.Name.EndsWith("_min", StringComparison.Ordinal);
                var metrica = regla.Name[..^4];
                if (!metricas.TryGetProperty(metrica, out var valor))
                {
                    fallas.Add($"{seccion.Name}: métrica desconocida {metrica}");
                    continue;
                }

                var v = valor.GetDouble();
                var limite = regla.Value.GetDouble();
                if (minimo ? v < limite : v > limite)
                {
                    fallas.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{seccion.Name}.{metrica} = {v:0.####} {(minimo ? "<" : ">")} {limite:0.####}"));
                }
            }
        }

        if (umbrales.RootElement.TryGetProperty("caida_minima_de_cita", out var caida) &&
            porContexto.TryGetValue("normal", out var normal))
        {
            foreach (var control in new[] { "vacio", "barajado" }.Where(porContexto.ContainsKey))
            {
                var diferencia = normal.GetProperty("cita").GetDouble() - porContexto[control].GetProperty("cita").GetDouble();
                if (diferencia < caida.GetDouble())
                {
                    fallas.Add(string.Create(CultureInfo.InvariantCulture,
                        $"la cita sólo cae {diferencia:0.####} con el contexto {control} (mínimo {caida.GetDouble():0.##})"));
                }
            }
        }

        return fallas;
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Perito.Application.Puertos;
using Perito.Asistente.Corpus;
using Perito.Asistente.Recuperacion;

namespace Perito.Asistente;

/// <summary>
/// El prompt del asistente: instrucciones de sistema y un mensaje con la descripción seudonimizada y las cláusulas
/// recuperadas, cada una con su id y metadatos. El formato es fijo para que el modelo extractivo (línea base) lea lo
/// mismo que lee un LLM.
/// </summary>
public static partial class Prompt
{
    /// <summary>Encabezado de la descripción.</summary>
    public const string SeccionDescripcion = "## Descripción del siniestro (seudonimizada)";

    /// <summary>Encabezado de las cláusulas.</summary>
    public const string SeccionClausulas = "## Cláusulas recuperadas";

    /// <summary>Nombre del complemento de la póliza en el kernel.</summary>
    public const string Complemento = "poliza";

    /// <summary>Nombre de la función de coberturas.</summary>
    public const string Funcion = "coberturas_contratadas";

    /// <summary>Instrucciones de sistema.</summary>
    public static readonly string Sistema = $$"""
        Eres el asistente de triaje de siniestros de la Aseguradora Ficticia del Valle. PROPONES; no decides: una
        persona revisa tu propuesta y las reglas de la aseguradora deciden cobertura, reserva y dictamen.

        Reglas:
        1. Usa sólo las cláusulas de «Cláusulas recuperadas». Cita la cláusula de cobertura con su id exacto (por
           ejemplo AUT-2.1-a). Si ninguna aplica, usa null.
        2. Antes de responder llama a la función {{Complemento}}-{{Funcion}} para saber qué coberturas tiene la póliza.
        3. "tipo" es el valor del campo «tipo» de la cláusula que citas.
        4. "datos_faltantes": de los «datos» que pide la cláusula citada, los que la descripción NO dice que ya se
           tienen. Valores posibles: Lugar, Fotografias, PresupuestoDeReparacion, DictamenDeBomberos,
           DenunciaAnteMinisterioPublico, InformeMedico, InventarioDeBienes, DatosDelTercero.
        5. "exclusiones": ids de las cláusulas de exclusión recuperadas que habría que verificar por lo que dice la
           descripción. Puede ir vacía.
        6. "senales": señales de riesgo que aparezcan en el texto, de entre SinTestigosNiEvidencia,
           BienesSinComprobante, RelatoContradictorio, ContratacionReciente, PresionPorPagoRapido.
        7. Las marcas entre corchetes, como [PERSONA_1] o [DIRECCION_1], son datos personales seudonimizados: no
           intentes adivinarlos ni los repitas.

        Responde SÓLO con un objeto JSON, sin texto alrededor:
        {"tipo": "...", "clausula": "...", "datos_faltantes": [], "exclusiones": [], "senales": [], "justificacion": "..."}
        """;

    /// <summary>El mensaje del usuario con la descripción y el contexto.</summary>
    public static string Mensaje(SolicitudAlAsistente solicitud, ContextoRecuperado contexto)
    {
        var sb = new StringBuilder();
        sb.Append(SeccionDescripcion).Append('\n').Append(solicitud.Descripcion.Texto).Append("\n\n");
        sb.Append(SeccionClausulas).Append('\n');
        if (contexto.Todos.Count == 0)
        {
            sb.Append("(ninguna)\n");
        }

        foreach (var f in contexto.Todos)
        {
            sb.Append('[').Append(f.Id).Append("] ").Append(f.Titulo);
            if (f.EsCobertura)
            {
                sb.Append(CultureInfo.InvariantCulture, $" · tipo: {f.Tipo} · cobertura: {f.Cobertura}")
                    .Append(" · datos: ").Append(string.Join(", ", f.Datos));
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $" · exclusión: {f.Exclusion}")
                    .Append(" · aplica: ").Append(string.Join(", ", f.Aplica));
            }

            sb.Append('\n').Append(f.Texto).Append("\n\n");
        }

        return sb.ToString();
    }

    /// <summary>Lee un mensaje con este formato (lo usa el modelo extractivo, que sólo ve el prompt).</summary>
    public static (string Descripcion, IReadOnlyList<Fragmento> Clausulas) Leer(string mensaje)
    {
        var i = mensaje.IndexOf(SeccionDescripcion, StringComparison.Ordinal);
        var j = mensaje.IndexOf(SeccionClausulas, StringComparison.Ordinal);
        if (i < 0 || j < i)
        {
            return (mensaje, []);
        }

        var descripcion = mensaje[(i + SeccionDescripcion.Length)..j].Trim();
        var clausulas = Clausula().Matches(mensaje[(j + SeccionClausulas.Length)..]).Select(m =>
        {
            var meta = m.Groups["meta"].Value.Split(" · ", StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split(": ", 2)).Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.Ordinal);
            var lista = (string llave) => meta.TryGetValue(llave, out var v)
                ? v.Split(", ", StringSplitOptions.RemoveEmptyEntries)
                : [];
            return new Fragmento(m.Groups["id"].Value, m.Groups["titulo"].Value.Trim(), "?",
                meta.GetValueOrDefault("cobertura"), meta.GetValueOrDefault("tipo"), meta.GetValueOrDefault("exclusión"),
                lista("aplica"), lista("datos"), m.Groups["texto"].Value.Trim());
        }).ToList();
        return (descripcion, clausulas);
    }

    [GeneratedRegex(@"^\[(?<id>[A-Z]{3}-[0-9.]+(?:-[a-z])?)\] (?<titulo>[^·\n]+)(?<meta>(?: · [^\n]+)?)\n(?<texto>[^\n]*)",
        RegexOptions.Multiline)]
    private static partial Regex Clausula();
}

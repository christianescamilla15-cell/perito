using System.Text;
using Perito.Asistente.Corpus;
using Perito.Domain.Producto;

namespace Perito.Asistente.Recuperacion;

/// <summary>Normalización del español para buscar: minúsculas, sin acentos, sin palabras vacías y con una raíz ligera
/// (quita terminaciones de plural, participio y conjugación frecuentes).</summary>
public static class Texto
{
    private static readonly HashSet<string> Vacias = new(StringComparer.Ordinal)
    {
        "a", "al", "algo", "ante", "antes", "aqui", "asi", "aun", "como", "con", "contra", "cual", "cuando", "de",
        "del", "desde", "donde", "dos", "el", "ella", "ellos", "en", "entre", "era", "es", "esa", "ese", "eso", "esta",
        "estaba", "este", "esto", "fue", "ha", "habia", "han", "hasta", "hay", "he", "la", "las", "le", "les", "lo",
        "los", "mas", "me", "mi", "mis", "muy", "nada", "ni", "no", "nos", "o", "otra", "otro", "para", "pero", "poco",
        "por", "porque", "que", "quien", "se", "sea", "ser", "si", "sin", "sobre", "solo", "su", "sus", "tambien",
        "te", "tengo", "tiene", "todo", "tu", "un", "una", "uno", "unos", "unas", "y", "ya", "yo", "puede", "cuando",
        "persona", "direccion", "telefono", "correo", "folio", "rfc", "curp", "clabe", "tarjeta",
    };

    private static readonly string[] Terminaciones =
    [
        "amientos", "imientos", "amiento", "imiento", "aciones", "iciones", "acion", "icion", "adoras", "adores",
        "adora", "ador", "aron", "ieron", "ando", "iendo", "ados", "adas", "idos", "idas", "ado", "ada", "ido", "ida",
        "aba", "ias", "ia", "es", "os", "as", "ar", "er", "ir", "o", "a", "e", "s",
    ];

    /// <summary>Minúsculas y sin acentos (tabla propia: con InvariantGlobalization no se descompone Unicode).</summary>
    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            sb.Append(char.ToLowerInvariant(c) switch
            {
                'á' => 'a',
                'é' => 'e',
                'í' => 'i',
                'ó' => 'o',
                'ú' or 'ü' => 'u',
                'ñ' => 'n',
                var x => x,
            });
        }

        return sb.ToString();
    }

    /// <summary>Las raíces de las palabras con contenido, en orden.</summary>
    public static IReadOnlyList<string> Raices(string texto)
    {
        var palabras = new List<string>();
        var actual = new StringBuilder();
        foreach (var c in Normalizar(texto).Append(' '))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                actual.Append(c);
                continue;
            }

            if (actual.Length > 0)
            {
                var palabra = actual.ToString();
                actual.Clear();
                if (palabra.Length > 2 && !Vacias.Contains(palabra) && !palabra.All(char.IsAsciiDigit))
                {
                    palabras.Add(Raiz(palabra));
                }
            }
        }

        return palabras;
    }

    /// <summary>Raíz ligera: quita la primera terminación que deje al menos tres letras.</summary>
    public static string Raiz(string palabra)
    {
        foreach (var t in Terminaciones)
        {
            if (palabra.EndsWith(t, StringComparison.Ordinal) && palabra.Length - t.Length >= 3)
            {
                return palabra[..^t.Length];
            }
        }

        return palabra;
    }
}

/// <summary>Lo que se recupera para una descripción: cláusulas de cobertura y de exclusión, por separado.</summary>
public sealed record ContextoRecuperado(IReadOnlyList<Fragmento> Coberturas, IReadOnlyList<Fragmento> Exclusiones)
{
    /// <summary>Todos los fragmentos, coberturas primero.</summary>
    public IReadOnlyList<Fragmento> Todos => [.. Coberturas, .. Exclusiones];

    /// <summary>Contexto vacío (control negativo).</summary>
    public static ContextoRecuperado Vacio { get; } = new([], []);
}

/// <summary>Recupera cláusulas para una descripción. Intercambiable: la evaluación lo sustituye para el control
/// negativo (contexto vacío o de otro caso).</summary>
public interface IRecuperadorDeClausulas
{
    /// <summary>Las mejores cláusulas del ramo para el texto.</summary>
    ContextoRecuperado Buscar(string texto, Ramo ramo);
}

/// <summary>
/// BM25 (k1 = 1.2, b = 0.75) sobre título y texto de cada cláusula, filtrado por el ramo de la póliza (metadato que la
/// aplicación conoce), con cuotas separadas: las 4 mejores coberturas y las 3 mejores exclusiones. Determinista: el
/// empate se rompe por el orden del documento.
/// </summary>
public sealed class RecuperadorBm25 : IRecuperadorDeClausulas
{
    /// <summary>Cuántas cláusulas de cobertura se recuperan.</summary>
    public const int CoberturasARecuperar = 4;

    /// <summary>Cuántas cláusulas de exclusión se recuperan.</summary>
    public const int ExclusionesARecuperar = 3;

    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly List<(Fragmento Fragmento, Dictionary<string, int> Frecuencias, int Largo, int Orden)> _indice;
    private readonly Dictionary<string, int> _documentosPorTermino = new(StringComparer.Ordinal);
    private readonly double _largoPromedio;

    /// <summary>Indexa los fragmentos dados (por omisión, las condiciones generales incrustadas).</summary>
    public RecuperadorBm25(IReadOnlyList<Fragmento>? fragmentos = null)
    {
        _indice = (fragmentos ?? LectorDeCondiciones.Fragmentos)
            .Where(f => f.EsCobertura || f.EsExclusion)
            .Select((f, i) =>
            {
                var raices = Texto.Raices(f.Titulo + " " + f.Texto);
                return (f, raices.GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
                    raices.Count, i);
            }).ToList();
        foreach (var termino in _indice.SelectMany(x => x.Frecuencias.Keys))
        {
            _documentosPorTermino[termino] = _documentosPorTermino.GetValueOrDefault(termino) + 1;
        }

        _largoPromedio = _indice.Count == 0 ? 1 : _indice.Average(x => x.Largo);
    }

    /// <inheritdoc />
    public ContextoRecuperado Buscar(string texto, Ramo ramo)
    {
        var consulta = Texto.Raices(texto).Distinct().ToList();
        var delRamo = _indice.Where(x => x.Fragmento.Ramo == ramo.ToString())
            .Select(x => (x.Fragmento, Puntaje: Puntaje(consulta, x.Frecuencias, x.Largo), x.Orden))
            .Where(x => x.Puntaje > 0)
            .OrderByDescending(x => x.Puntaje).ThenBy(x => x.Orden)
            .ToList();
        return new ContextoRecuperado(
            delRamo.Where(x => x.Fragmento.EsCobertura).Take(CoberturasARecuperar).Select(x => x.Fragmento).ToList(),
            delRamo.Where(x => x.Fragmento.EsExclusion).Take(ExclusionesARecuperar).Select(x => x.Fragmento).ToList());
    }

    private double Puntaje(IEnumerable<string> consulta, Dictionary<string, int> frecuencias, int largo)
    {
        var n = _indice.Count;
        var puntaje = 0.0;
        foreach (var termino in consulta)
        {
            if (!frecuencias.TryGetValue(termino, out var f))
            {
                continue;
            }

            var df = _documentosPorTermino[termino];
            var idf = Math.Log(1 + (n - df + 0.5) / (df + 0.5));
            puntaje += idf * f * (K1 + 1) / (f + K1 * (1 - B + B * largo / _largoPromedio));
        }

        return puntaje;
    }
}

using System.Text.RegularExpressions;

namespace Perito.Domain.Tests;

/// <summary>
/// docs/reglas.md es la compuerta: cada fila de sus tablas nombra al menos una prueba como `archivo::Metodo`, y cada
/// referencia existe como [Fact] o [Theory] sin Skip. Así la tabla no puede afirmar una prueba que no existe o que se
/// apagó (las sondas genéricas no saben leer C#).
/// </summary>
public partial class ReglasTests
{
    private static readonly string Raiz = CondicionesGeneralesTests.Raiz();

    private static IEnumerable<(int Linea, string Fila, List<(string Archivo, string Metodo)> Referencias)> Filas()
    {
        var lineas = File.ReadAllLines(Path.Join(Raiz, "docs", "reglas.md"));
        var enTabla = false;
        for (var i = 0; i < lineas.Length; i++)
        {
            if (Separador().IsMatch(lineas[i]))
            {
                enTabla = i > 0 && lineas[i - 1].Contains("Prueba", StringComparison.Ordinal);
                continue;
            }

            if (!lineas[i].StartsWith('|'))
            {
                enTabla = false;
                continue;
            }

            if (enTabla)
            {
                yield return (i + 1, lineas[i].Split('|')[1].Trim(), Referencia().Matches(lineas[i])
                    .Select(m => (m.Groups["archivo"].Value, m.Groups["metodo"].Value)).ToList());
            }
        }
    }

    [Fact]
    public void Cada_fila_de_la_tabla_de_reglas_nombra_al_menos_una_prueba()
    {
        var filas = Filas().ToList();
        Assert.True(filas.Count >= 50, $"sólo {filas.Count} filas: ¿cambió el formato de docs/reglas.md?");
        Assert.All(filas, f => Assert.True(f.Referencias.Count > 0, $"línea {f.Linea} ({f.Fila}) sin prueba"));
    }

    [Fact]
    public void Cada_prueba_que_nombra_la_tabla_existe_y_no_esta_apagada()
    {
        var faltan = new List<string>();
        foreach (var (archivo, metodo) in Filas().SelectMany(f => f.Referencias).Distinct())
        {
            var ruta = Path.Join(Raiz, archivo);
            if (!File.Exists(ruta))
            {
                faltan.Add($"{archivo}: no existe");
                continue;
            }

            var declaracion = new Regex(
                @"\[(?<atributo>Fact|Theory)(?<args>\([^)]*\))?\][ \t]*\r?\n(?:[ \t]*\[.*\][^\n]*\r?\n)*[ \t]*" +
                @"public (?:async Task|void) " +
                Regex.Escape(metodo) + @"\(");
            var m = declaracion.Match(File.ReadAllText(ruta));
            if (!m.Success)
            {
                faltan.Add($"{archivo}::{metodo}: no es una prueba de xUnit");
            }
            else if (m.Groups["args"].Value.Contains("Skip", StringComparison.Ordinal))
            {
                faltan.Add($"{archivo}::{metodo}: está apagada con Skip");
            }
        }

        Assert.Empty(faltan);
    }

    /// <summary>Control: el verificador sí reconoce una referencia inventada.</summary>
    [Fact]
    public void Una_referencia_inventada_no_pasaria()
    {
        var texto = File.ReadAllText(Path.Join(Raiz, "tests", "Perito.Domain.Tests", "ReglasTests.cs"));
        Assert.DoesNotMatch(new Regex(@"\[(Fact|Theory)\]\s*public void Prueba_que_no_existe\("), texto);
        Assert.Matches(Referencia(), "`tests/Perito.Domain.Tests/ReglasTests.cs::Una_referencia_inventada_no_pasaria`");
    }

    [GeneratedRegex(@"^\|\s*:?-{3,}")]
    private static partial Regex Separador();

    [GeneratedRegex(@"`(?<archivo>tests/[\w./-]+\.cs)::(?<metodo>\w+)`")]
    private static partial Regex Referencia();
}

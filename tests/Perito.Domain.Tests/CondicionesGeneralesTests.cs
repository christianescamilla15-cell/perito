using System.Globalization;
using System.Text.RegularExpressions;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;

namespace Perito.Domain.Tests;

/// <summary>
/// Las condiciones generales (docs/condiciones) son la fuente de las reglas y el corpus que cita el asistente: el
/// catálogo del código y los metadatos del documento no pueden separarse, y los ejemplos de GEN-7 son el oráculo del
/// cálculo de la indemnización.
/// </summary>
public partial class CondicionesGeneralesTests
{
    private static readonly string Documento = File.ReadAllText(Path.Join(Raiz(), "docs", "condiciones",
        "condiciones-generales.md"));

    [Fact]
    public void Cada_tipo_del_catalogo_esta_en_su_clausula_con_el_mismo_ramo_cobertura_y_datos()
    {
        var fragmentos = Fragmentos().Where(f => f.Meta.ContainsKey("tipo")).ToList();
        Assert.Equal(Catalogo.Tipos.Count, fragmentos.Count);
        foreach (var definicion in Catalogo.Tipos)
        {
            var f = Assert.Single(fragmentos, x => x.Meta["tipo"] == definicion.Tipo.ToString());
            Assert.Equal(definicion.Clausula, f.Id);
            Assert.Equal(definicion.Ramo.ToString(), f.Meta["ramo"]);
            Assert.Equal(definicion.Cobertura.ToString(), f.Meta["cobertura"]);
            Assert.Equal(definicion.Datos.Select(d => d.ToString()).Order(), Lista(f.Meta["datos"]).Order());
        }
    }

    [Fact]
    public void Cada_exclusion_del_catalogo_esta_en_su_clausula_con_las_coberturas_a_las_que_aplica()
    {
        var fragmentos = Fragmentos().Where(f => f.Meta.ContainsKey("exclusion")).ToList();
        Assert.Equal(Catalogo.Exclusiones.Count, fragmentos.Count);
        foreach (var definicion in Catalogo.Exclusiones)
        {
            var f = Assert.Single(fragmentos, x => x.Meta["exclusion"] == definicion.Exclusion.ToString());
            Assert.Equal(definicion.Clausula, f.Id);
            Assert.Equal(definicion.Ramo.ToString(), f.Meta["ramo"]);
            Assert.Equal(definicion.Aplica.Select(c => c.ToString()).Order(), Lista(f.Meta["aplica"]).Order());
        }
    }

    [Fact]
    public void Las_clausulas_generales_que_cita_el_dominio_existen()
    {
        var ids = Fragmentos().Select(f => f.Id).ToHashSet();
        string[] citadas = [Clausulas.Definiciones, Clausulas.Vigencia, Clausulas.Prima, Clausulas.Aviso,
            Clausulas.SumaAsegurada, Clausulas.Deducible, Clausulas.Calculo, Clausulas.Documentacion, Clausulas.Fraude,
            Clausulas.Pago, Clausulas.Rechazo];
        Assert.All(citadas, c => Assert.Contains(c, ids));
    }

    /// <summary>GEN-7: los seis ejemplos del documento, leídos de su tabla, son el oráculo del cálculo.</summary>
    [Fact]
    public void Los_ejemplos_de_GEN_7_se_cumplen()
    {
        var filas = FilaDeEjemplo().Matches(Documento);
        Assert.Equal(6, filas.Count);
        foreach (Match fila in filas)
        {
            var perdida = Monto(fila.Groups["perdida"].Value);
            var suma = Monto(fila.Groups["suma"].Value);
            var deducible = Deducible(fila.Groups["deducible"].Value.Trim());
            var coaseguro = decimal.Parse(fila.Groups["coaseguro"].Value, CultureInfo.InvariantCulture) / 100m;
            var cobertura = new CoberturaContratada(CodigoCobertura.DanosMateriales, suma, deducible, coaseguro);

            Assert.True(Monto(fila.Groups["indemnizacion"].Value) == CalculoDeIndemnizacion.Calcular(perdida, cobertura),
                $"ejemplo {fila.Groups["n"].Value}");
        }
    }

    private static Deducible Deducible(string texto)
    {
        if (texto.StartsWith("fijo ", StringComparison.Ordinal))
        {
            return new Deducible(TipoDeDeducible.Fijo, Monto(texto[5..]));
        }

        var porcentaje = decimal.Parse(texto.Split(' ')[0], CultureInfo.InvariantCulture) / 100m;
        return texto.EndsWith("de la suma", StringComparison.Ordinal)
            ? new Deducible(TipoDeDeducible.PorcentajeSuma, porcentaje)
            : new Deducible(TipoDeDeducible.PorcentajePerdida, porcentaje);
    }

    private static decimal Monto(string texto) =>
        decimal.Parse(texto.Replace(",", string.Empty, StringComparison.Ordinal).Trim(), CultureInfo.InvariantCulture);

    private static IEnumerable<string> Lista(string valor) =>
        valor.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    internal static IReadOnlyList<(string Id, Dictionary<string, string> Meta)> Fragmentos() =>
        Encabezado().Matches(Documento).Select(m => (m.Groups["id"].Value,
            m.Groups["meta"].Value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(par => par.Split(':', 2, StringSplitOptions.TrimEntries))
                .ToDictionary(par => par[0], par => par[1]))).ToList();

    internal static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Perito.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    [GeneratedRegex(@"^### (?<id>[A-Z]{3}-[0-9.]+(?:-[a-z])?) · .+\n<!-- (?<meta>.+) -->$", RegexOptions.Multiline)]
    private static partial Regex Encabezado();

    [GeneratedRegex(
        @"^\| (?<n>\d) \| (?<perdida>[\d,.]+) \| (?<suma>[\d,.]+) \| (?<deducible>[^|]+)\| (?<coaseguro>\d+) % \| (?<indemnizacion>[\d,.]+) \|$",
        RegexOptions.Multiline)]
    private static partial Regex FilaDeEjemplo();
}

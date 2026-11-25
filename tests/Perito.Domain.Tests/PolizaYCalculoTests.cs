using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using static Perito.Domain.Tests.Escenario;

namespace Perito.Domain.Tests;

/// <summary>Vigencia (GEN-2), prima y periodo de gracia (GEN-3), carátula, deducible (GEN-6) y cálculo (GEN-7).</summary>
public class PolizaYCalculoTests
{
    [Theory]
    [InlineData(2026, 1, 1, null)]       // primer día, inclusive
    [InlineData(2026, 12, 31, null)]     // último día, inclusive
    [InlineData(2025, 12, 31, MotivoDeNoElegibilidad.FueraDeVigencia)]
    [InlineData(2027, 1, 1, MotivoDeNoElegibilidad.FueraDeVigencia)]
    public void La_vigencia_incluye_sus_dos_extremos(int a, int m, int d, MotivoDeNoElegibilidad? esperado) =>
        Assert.Equal(esperado, AutoAmplia().AmparaLaFecha(new DateOnly(a, m, d)));

    [Fact]
    public void Lo_ocurrido_desde_la_cancelacion_no_se_ampara_y_lo_anterior_si()
    {
        var poliza = AutoAmplia();
        Exito(poliza.Cancelar(new DateOnly(2026, 5, 1)));
        Assert.Null(poliza.AmparaLaFecha(new DateOnly(2026, 4, 30)));
        Assert.Equal(MotivoDeNoElegibilidad.PolizaCancelada, poliza.AmparaLaFecha(new DateOnly(2026, 5, 1)));
        Falla(poliza.Cancelar(new DateOnly(2026, 6, 1)), "ya_cancelada");
    }

    [Fact]
    public void El_dia_treinta_del_periodo_de_gracia_ampara_y_el_treinta_y_uno_no()
    {
        var poliza = AutoAmplia(primaHasta: new DateOnly(2026, 3, 31));
        Assert.Null(poliza.AmparaLaFecha(new DateOnly(2026, 4, 30)));
        Assert.Equal(MotivoDeNoElegibilidad.PrimaVencida, poliza.AmparaLaFecha(new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void Una_cobertura_que_no_esta_en_la_caratula_no_es_elegible_y_cita_GEN_1()
    {
        var elegibilidad = AutoLimitada().Elegibilidad(Hoy, CodigoCobertura.DanosMateriales);
        Assert.Equal(MotivoDeNoElegibilidad.CoberturaNoContratada, elegibilidad.Motivo);
        Assert.Equal("GEN-1", elegibilidad.Clausula);
    }

    [Fact]
    public void La_fecha_manda_sobre_la_cobertura_en_el_motivo()
    {
        var elegibilidad = AutoLimitada().Elegibilidad(new DateOnly(2027, 2, 1), CodigoCobertura.DanosMateriales);
        Assert.Equal(MotivoDeNoElegibilidad.FueraDeVigencia, elegibilidad.Motivo);
        Assert.Equal("GEN-2", elegibilidad.Clausula);
    }

    [Theory]
    [InlineData("fin_antes_del_inicio")]
    [InlineData("cobertura_de_otro_ramo")]
    [InlineData("cobertura_repetida")]
    [InlineData("coaseguro_mayor_a_la_mitad")]
    [InlineData("porcentaje_mayor_a_uno")]
    [InlineData("sin_coberturas")]
    public void Una_caratula_invalida_no_se_emite(string caso)
    {
        var (fin, coberturas, codigo) = CaratulaInvalida(caso);
        Falla(Poliza.Emitir(Guid.NewGuid(), "AFV-AUT-1", Ramo.Auto, Guid.NewGuid(), Inicio, fin, Fin, coberturas),
            codigo);
    }

    private static (DateOnly Fin, CoberturaContratada[] Coberturas, string Codigo) CaratulaInvalida(string caso)
    {
        var dm = Cobertura(CodigoCobertura.DanosMateriales, 100_000m, TipoDeDeducible.Fijo, 1_000m);
        return caso switch
        {
            "fin_antes_del_inicio" => (Inicio.AddDays(-1), [dm], "vigencia_invalida"),
            "cobertura_de_otro_ramo" => (Fin, [dm, Cobertura(CodigoCobertura.Incendio, 1m, TipoDeDeducible.Fijo, 0)],
                "cobertura_de_otro_ramo"),
            "cobertura_repetida" => (Fin, [dm, dm], "cobertura_repetida"),
            "coaseguro_mayor_a_la_mitad" => (Fin, [dm with { Coaseguro = 0.6m }], "cobertura_invalida"),
            "porcentaje_mayor_a_uno" => (Fin,
                [dm with { Deducible = new Deducible(TipoDeDeducible.PorcentajeSuma, 1.5m) }], "cobertura_invalida"),
            _ => (Fin, [], "caratula_vacia"),
        };
    }

    [Theory]
    [InlineData(TipoDeDeducible.Fijo, 4_000, 4_000)]
    [InlineData(TipoDeDeducible.PorcentajeSuma, 0.05, 15_000)]     // 5 % de 300,000
    [InlineData(TipoDeDeducible.PorcentajePerdida, 0.20, 8_000)]   // 20 % de 40,000
    public void El_deducible_se_calcula_segun_su_tipo(TipoDeDeducible tipo, double valor, double esperado) =>
        Assert.Equal((decimal)esperado, new Deducible(tipo, (decimal)valor).Monto(40_000m, 300_000m));

    [Fact]
    public void Una_perdida_igual_al_deducible_no_se_indemniza()
    {
        var cobertura = Cobertura(CodigoCobertura.DanosPorAgua, 150_000m, TipoDeDeducible.Fijo, 3_000m);
        Assert.Equal(0m, CalculoDeIndemnizacion.Calcular(3_000m, cobertura));
        Assert.Equal(0.01m, CalculoDeIndemnizacion.Calcular(3_000.01m, cobertura));
    }

    /// <summary>GEN-7: «la mitad hacia arriba». 1,000.05 × 50 % = 500.025 → 500.03 (al par sería 500.02).</summary>
    [Fact]
    public void El_centavo_se_redondea_la_mitad_hacia_arriba_no_al_par()
    {
        var cobertura = Cobertura(CodigoCobertura.Incendio, 1_000_000m, TipoDeDeducible.Fijo, 0m, coaseguro: 0.5m);
        Assert.Equal(500.03m, CalculoDeIndemnizacion.Calcular(1_000.05m, cobertura));
    }

    [Fact]
    public void Una_perdida_negativa_es_un_error_de_programacion() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculoDeIndemnizacion.Calcular(-1m,
            Cobertura(CodigoCobertura.Incendio, 1m, TipoDeDeducible.Fijo, 0m)));
}

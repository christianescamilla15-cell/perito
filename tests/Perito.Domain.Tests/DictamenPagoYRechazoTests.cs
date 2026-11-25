using Perito.Domain.Ajustadores;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using static Perito.Domain.Tests.Escenario;

namespace Perito.Domain.Tests;

/// <summary>Dictamen determinista, pago (GEN-10, separación de funciones) y rechazo (GEN-11).</summary>
public class DictamenPagoYRechazoTests
{
    private static EntradaDeDictamen Entrada(decimal perdida, params CodigoExclusion[] exclusiones) =>
        new(perdida, exclusiones, null);

    [Fact]
    public void Un_siniestro_elegible_con_documentos_y_sin_exclusiones_es_procedente_por_el_calculo()
    {
        var s = Dictaminado(AutoAmplia(), perdida: 60_000m);
        var d = s.Dictamen!;
        Assert.True(d.Procedente);
        Assert.Equal(42_500m, d.Indemnizacion);    // 60,000 − 17,500
        Assert.Equal("AUT-2.1-a", d.Clausula);
        Assert.Equal(Hoy.AddDays(30), s.FechaLimiteDePago);
        Assert.Equal(EstadoSiniestro.Dictaminado, s.Estado);
    }

    [Fact]
    public void Sin_la_documentacion_del_tipo_no_se_dictamina()
    {
        var poliza = AutoAmplia();
        var s = Reservado(poliza, conDocumentos: false);
        Exito(s.RegistrarDatoRecibido(DatoRequerido.Lugar));
        var r = s.Dictaminar(poliza, Entrada(50_000m), "ajustador.uno", Hoy, Ahora);
        Falla(r, "documentacion_incompleta");
        Assert.Contains("Fotografias", r.Error!.Mensaje, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fuera_de_vigencia", MotivoDeDictamen.FueraDeVigencia, "GEN-2")]
    [InlineData("prima_vencida", MotivoDeDictamen.PrimaVencida, "GEN-3")]
    [InlineData("no_contratada", MotivoDeDictamen.CoberturaNoContratada, "GEN-1")]
    public void Un_siniestro_no_elegible_se_dictamina_improcedente_sin_reserva_ni_documentos(string caso,
        MotivoDeDictamen motivo, string clausula)
    {
        var (poliza, ocurrencia) = caso switch
        {
            "fuera_de_vigencia" => (AutoAmplia(), new DateOnly(2025, 12, 20)),
            "prima_vencida" => (AutoAmplia(primaHasta: new DateOnly(2026, 2, 28)), new DateOnly(2026, 4, 1)),
            _ => (AutoLimitada(), Hoy.AddDays(-1)),
        };
        var s = Asignado(poliza, ocurrencia: ocurrencia);
        Exito(s.Dictaminar(poliza, Entrada(80_000m), "ajustador.uno", Hoy, Ahora));
        Assert.False(s.Dictamen!.Procedente);
        Assert.Equal((motivo, clausula, 0m), (s.Dictamen.Motivo, s.Dictamen.Clausula, s.Dictamen.Indemnizacion));
        Assert.Null(s.FechaLimiteDePago);
    }

    [Fact]
    public void Una_poliza_cancelada_antes_de_la_ocurrencia_se_dictamina_con_GEN_2()
    {
        var poliza = AutoAmplia();
        Exito(poliza.Cancelar(new DateOnly(2026, 5, 1)));
        var s = Asignado(poliza);
        Exito(s.Dictaminar(poliza, Entrada(80_000m), "ajustador.uno", Hoy, Ahora));
        Assert.Equal((MotivoDeDictamen.PolizaCancelada, "GEN-2"), (s.Dictamen!.Motivo, s.Dictamen.Clausula));
    }

    [Fact]
    public void Una_exclusion_confirmada_que_aplica_hace_improcedente_y_cita_su_clausula()
    {
        var poliza = AutoAmplia();
        var s = Reservado(poliza);
        Exito(s.Dictaminar(poliza, Entrada(80_000m, CodigoExclusion.ConductorBajoInfluencia), "ajustador.uno", Hoy,
            Ahora));
        Assert.Equal((false, MotivoDeDictamen.Exclusion, "AUT-4.1"),
            (s.Dictamen!.Procedente, s.Dictamen.Motivo, s.Dictamen.Clausula));
    }

    [Fact]
    public void Una_exclusion_que_no_aplica_a_la_cobertura_se_rechaza_como_dato()
    {
        var poliza = AutoAmplia();
        var s = Reservado(poliza);
        Falla(s.Dictaminar(poliza, Entrada(80_000m, CodigoExclusion.RoboSinViolencia), "ajustador.uno", Hoy, Ahora),
            "exclusion_no_aplica");
        Assert.Equal(EstadoSiniestro.Reservado, s.Estado);
    }

    [Fact]
    public void Con_indicios_no_se_dictamina_sin_el_resultado_de_la_investigacion_y_el_fraude_comprobado_es_GEN_9()
    {
        var poliza = AutoAmplia();
        var s = Avisado(poliza, ocurrencia: Inicio.AddDays(2), aviso: Inicio.AddDays(3));
        Exito(s.Triar(TipoDeSiniestro.Colision, poliza, 2, "ajustador.uno", null, Ahora));
        Exito(s.AsignarAjustador(AjustadorDe(Especialidad.Investigacion), 0, Ahora));
        Exito(s.ConstituirReserva(poliza, 100_000m, Ahora));
        foreach (var dato in Catalogo.De(TipoDeSiniestro.Colision).Datos)
        {
            Exito(s.RegistrarDatoRecibido(dato));
        }

        Falla(s.Dictaminar(poliza, Entrada(50_000m), "inv.uno", Hoy, Ahora), "investigacion_pendiente");
        Exito(s.Dictaminar(poliza, new EntradaDeDictamen(50_000m, [], ResultadoDeInvestigacion.FraudeComprobado),
            "inv.uno", Hoy, Ahora));
        Assert.Equal((MotivoDeDictamen.Fraude, "GEN-9"), (s.Dictamen!.Motivo, s.Dictamen.Clausula));
    }

    [Fact]
    public void Una_perdida_que_no_supera_el_deducible_es_improcedente_aun_sin_reserva()
    {
        var poliza = AutoAmplia();
        var s = Asignado(poliza);
        foreach (var dato in Catalogo.De(TipoDeSiniestro.Colision).Datos)
        {
            Exito(s.RegistrarDatoRecibido(dato));
        }

        Exito(s.Dictaminar(poliza, Entrada(17_500m), "ajustador.uno", Hoy, Ahora));
        Assert.Equal((MotivoDeDictamen.PerdidaMenorAlDeducible, "GEN-6"), (s.Dictamen!.Motivo, s.Dictamen.Clausula));
    }

    [Fact]
    public void Un_dictamen_procedente_necesita_reserva_constituida()
    {
        var poliza = AutoAmplia();
        var s = Asignado(poliza);
        foreach (var dato in Catalogo.De(TipoDeSiniestro.Colision).Datos)
        {
            Exito(s.RegistrarDatoRecibido(dato));
        }

        Falla(s.Dictaminar(poliza, Entrada(60_000m), "ajustador.uno", Hoy, Ahora), "reserva_requerida");
        Assert.Equal(EstadoSiniestro.Asignado, s.Estado);
    }

    [Fact]
    public void Si_la_indemnizacion_excede_la_reserva_primero_se_ajusta()
    {
        var poliza = AutoAmplia();
        var s = Reservado(poliza, estimacion: 50_000m);     // reserva 32,500
        Falla(s.Dictaminar(poliza, Entrada(120_000m), "ajustador.uno", Hoy, Ahora), "reserva_insuficiente");
        Exito(s.AjustarReserva(poliza, 102_500m, "peritaje del taller", Ahora));
        Exito(s.Dictaminar(poliza, Entrada(120_000m), "ajustador.uno", Hoy, Ahora));
        Assert.Equal(102_500m, s.Dictamen!.Indemnizacion);
    }

    [Fact]
    public void Quien_dictamina_no_puede_pagar_el_mismo_siniestro()
    {
        var s = Dictaminado(AutoAmplia());
        Falla(s.Pagar("SPEI-0001", "ajustador.uno", Hoy, Ahora), "separacion_de_funciones");
        Exito(s.Pagar("SPEI-0001", "tesoreria.uno", Hoy, Ahora));
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void Pagar_despues_de_treinta_dias_del_dictamen_queda_marcado_fuera_de_plazo(int dias, bool fueraDePlazo)
    {
        var s = Dictaminado(AutoAmplia());
        Exito(s.Pagar("SPEI-0002", "tesoreria.uno", Hoy.AddDays(dias), Ahora));
        Assert.Equal(fueraDePlazo, s.Pago!.FueraDePlazo);
    }

    [Fact]
    public void El_pago_libera_toda_la_reserva_y_su_evento_lo_dice()
    {
        var s = Dictaminado(AutoAmplia(), perdida: 60_000m);      // reserva 82,500, indemnización 42,500
        Exito(s.Pagar("SPEI-0003", "tesoreria.uno", Hoy, Ahora));
        Assert.Equal(0m, s.Reserva);
        Assert.Equal(TipoDeMovimiento.Liberacion, s.Movimientos[^1].Tipo);
        var evento = Assert.IsType<PagoEmitido>(s.Eventos[^1]);
        Assert.Equal((42_500m, 82_500m), (evento.Monto, evento.ReservaLiberada));
        Assert.Equal(EstadoSiniestro.Pagado, s.Estado);
        Assert.False(s.EstaAbierto);
    }

    [Fact]
    public void Un_dictamen_improcedente_no_se_paga_se_rechaza_con_su_clausula_y_libera_la_reserva()
    {
        var poliza = AutoAmplia();
        var s = Reservado(poliza);
        Exito(s.Dictaminar(poliza, Entrada(80_000m, CodigoExclusion.Competencias), "ajustador.uno", Hoy, Ahora));
        Falla(s.Pagar("SPEI-0004", "tesoreria.uno", Hoy, Ahora), "transicion_invalida");
        Exito(s.Rechazar(Hoy, Ahora));
        var evento = Assert.IsType<SiniestroRechazado>(s.Eventos[^1]);
        Assert.Equal(("AUT-4.4", 82_500m), (evento.Clausula, evento.ReservaLiberada));
        Assert.Equal((EstadoSiniestro.Rechazado, 0m), (s.Estado, s.Reserva));
    }

    [Fact]
    public void Un_dictamen_procedente_no_se_rechaza() =>
        Falla(Dictaminado(AutoAmplia()).Rechazar(Hoy, Ahora), "transicion_invalida");
}

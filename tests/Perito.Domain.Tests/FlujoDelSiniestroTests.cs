using Perito.Domain.Ajustadores;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using static Perito.Domain.Tests.Escenario;

namespace Perito.Domain.Tests;

/// <summary>Aviso (GEN-4), triaje con elegibilidad e indicios (GEN-9), asignación, documentación (GEN-8) y reserva.</summary>
public class FlujoDelSiniestroTests
{
    [Fact]
    public void El_aviso_no_acepta_una_ocurrencia_posterior_al_aviso() =>
        Falla(Siniestro.Avisar(Guid.NewGuid(), AutoAmplia(), Hoy.AddDays(1), Hoy, "Descripción suficientemente larga.",
            null, null, Ahora), "ocurrencia_futura");

    [Theory]
    [InlineData("corta")]
    [InlineData("                                      ")]
    public void El_aviso_exige_una_descripcion_de_al_menos_veinte_caracteres(string descripcion) =>
        Falla(Siniestro.Avisar(Guid.NewGuid(), AutoAmplia(), Hoy, Hoy, descripcion, null, null, Ahora),
            "descripcion_invalida");

    [Fact]
    public void El_aviso_emite_su_evento_con_identificadores_y_ramo()
    {
        var poliza = AutoAmplia();
        var s = Avisado(poliza);
        var evento = Assert.IsType<SiniestroAvisado>(Assert.Single(s.Eventos));
        Assert.Equal((s.Id, poliza.Id, Ramo.Auto), (evento.SiniestroId, evento.PolizaId, evento.Ramo));
        Assert.Equal(EstadoSiniestro.Avisado, s.Estado);
    }

    [Fact]
    public void El_triaje_deriva_la_cobertura_del_catalogo_y_no_de_quien_tria()
    {
        var poliza = AutoAmplia();
        var s = Triado(poliza, TipoDeSiniestro.RoturaDeCristales);
        Assert.Equal(CodigoCobertura.Cristales, s.Cobertura);
        Assert.True(s.EsElegible);
        Assert.Equal(EstadoSiniestro.Triado, s.Estado);
    }

    [Fact]
    public void Un_tipo_de_otro_ramo_no_se_acepta_en_el_triaje()
    {
        var poliza = AutoAmplia();
        var s = Avisado(poliza);
        Falla(s.Triar(TipoDeSiniestro.DanosPorAgua, poliza, 0, "ajustador.uno", null, Ahora), "tipo_de_otro_ramo");
        Assert.Equal(EstadoSiniestro.Avisado, s.Estado);
    }

    [Fact]
    public void Una_cobertura_no_contratada_se_tria_como_no_elegible()
    {
        var s = Triado(AutoLimitada(), TipoDeSiniestro.Colision);
        Assert.False(s.EsElegible);
        Assert.Equal(MotivoDeNoElegibilidad.CoberturaNoContratada, s.MotivoNoElegible);
        Assert.False(Assert.IsType<SiniestroTriado>(s.Eventos[^1]).Elegible);
    }

    [Theory]
    [InlineData(14, true)]   // día 14 desde el inicio: dentro de los primeros quince
    [InlineData(15, false)]
    public void El_siniestro_en_los_primeros_quince_dias_de_vigencia_es_indicio(int dias, bool indicio)
    {
        var poliza = AutoAmplia();
        var s = Triado(poliza, ocurrencia: Inicio.AddDays(dias));
        Assert.Equal(indicio, s.Indicios.Contains(IndicioDeFraude.SiniestroTemprano));
    }

    [Theory]
    [InlineData(5, false)]   // quinto día natural: todavía en plazo
    [InlineData(6, true)]
    public void El_aviso_despues_de_cinco_dias_naturales_es_indicio(int dias, bool indicio)
    {
        var poliza = AutoAmplia();
        var s = Avisado(poliza, ocurrencia: Hoy.AddDays(-dias), aviso: Hoy);
        Exito(s.Triar(TipoDeSiniestro.Colision, poliza, 0, "ajustador.uno", null, Ahora));
        Assert.Equal(indicio, s.Indicios.Contains(IndicioDeFraude.AvisoTardio));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Dos_siniestros_previos_en_doce_meses_son_indicio(int previos, bool indicio) =>
        Assert.Equal(indicio, Triado(AutoAmplia(), previos: previos).Indicios.Contains(IndicioDeFraude.FrecuenciaAlta));

    [Theory]
    [InlineData(314_999.99, false)]  // 90 % de 350,000 = 315,000
    [InlineData(315_000.00, true)]
    public void Una_estimacion_del_noventa_por_ciento_de_la_suma_es_indicio(double estimacion, bool indicio)
    {
        var poliza = AutoAmplia();
        var s = Avisado(poliza, estimacion: (decimal)estimacion);
        Exito(s.Triar(TipoDeSiniestro.Colision, poliza, 0, "ajustador.uno", null, Ahora));
        Assert.Equal(indicio, s.Indicios.Contains(IndicioDeFraude.MontoCercanoASuma));
    }

    [Fact]
    public void Dos_indicios_obligan_a_investigacion_y_uno_no()
    {
        Assert.False(Triado(AutoAmplia(), previos: 2).RequiereInvestigacion);
        var poliza = AutoAmplia();
        var dos = Avisado(poliza, ocurrencia: Inicio.AddDays(3), aviso: Inicio.AddDays(4));   // temprano + frecuencia
        Exito(dos.Triar(TipoDeSiniestro.Colision, poliza, 2, "ajustador.uno", null, Ahora));
        Assert.Equal(2, dos.Indicios.Count);
        Assert.True(dos.RequiereInvestigacion);
    }

    /// <summary>Lo que diga el asistente no cambia el triaje: con o sin propuesta, mismo resultado; sólo queda cuál fue.</summary>
    [Fact]
    public void Aceptar_una_propuesta_registra_cual_fue_pero_no_cambia_el_resultado_del_triaje()
    {
        var poliza = AutoLimitada();
        var sin = Avisado(poliza);
        var con = Avisado(poliza);
        var propuesta = Guid.NewGuid();
        Exito(sin.Triar(TipoDeSiniestro.Colision, poliza, 0, "ajustador.uno", null, Ahora));
        Exito(con.Triar(TipoDeSiniestro.Colision, poliza, 0, "ajustador.uno", propuesta, Ahora));

        Assert.Equal((sin.Cobertura, sin.MotivoNoElegible, sin.RequiereInvestigacion),
            (con.Cobertura, con.MotivoNoElegible, con.RequiereInvestigacion));
        Assert.Equal(propuesta, con.PropuestaAceptada);
        Assert.Null(sin.PropuestaAceptada);
    }

    [Fact]
    public void La_asignacion_exige_ajustador_activo_de_la_especialidad_del_ramo()
    {
        var s = Triado(AutoAmplia());
        Falla(s.AsignarAjustador(AjustadorDe(Especialidad.Hogar), 0, Ahora), "especialidad_incorrecta");
        var inactivo = AjustadorDe(Especialidad.Auto);
        inactivo.Desactivar();
        Falla(s.AsignarAjustador(inactivo, 0, Ahora), "ajustador_inactivo");
        Exito(s.AsignarAjustador(AjustadorDe(Especialidad.Auto), 0, Ahora));
    }

    [Fact]
    public void Con_investigacion_solo_un_investigador_puede_recibir_el_caso()
    {
        var s = Triado(AutoAmplia(), ocurrencia: Inicio.AddDays(3), previos: 2);
        Falla(s.AsignarAjustador(AjustadorDe(Especialidad.Auto), 0, Ahora), "especialidad_incorrecta");
        Exito(s.AsignarAjustador(AjustadorDe(Especialidad.Investigacion), 0, Ahora));
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    public void Un_ajustador_lleva_a_lo_mas_diez_casos_abiertos(int abiertos, bool asigna)
    {
        var s = Triado(AutoAmplia());
        var r = s.AsignarAjustador(AjustadorDe(Especialidad.Auto), abiertos, Ahora);
        if (asigna)
        {
            Exito(r);
        }
        else
        {
            Falla(r, "carga_completa");
        }
    }

    [Fact]
    public void Solo_se_registran_los_datos_que_pide_el_tipo_y_repetirlos_no_cambia_nada()
    {
        var s = Asignado(AutoAmplia(), TipoDeSiniestro.RoboTotal);
        Falla(s.RegistrarDatoRecibido(DatoRequerido.InformeMedico), "dato_no_requerido");
        Exito(s.RegistrarDatoRecibido(DatoRequerido.Lugar));
        Exito(s.RegistrarDatoRecibido(DatoRequerido.Lugar));
        Assert.Equal(new[] { DatoRequerido.DenunciaAnteMinisterioPublico }, s.DatosPendientes);
    }

    [Fact]
    public void La_reserva_es_la_perdida_estimada_neta_de_deducible()
    {
        var poliza = AutoAmplia();
        var s = Asignado(poliza);
        Exito(s.ConstituirReserva(poliza, 100_000m, Ahora));   // 100,000 − 5 % de 350,000
        Assert.Equal(82_500m, s.Reserva);
        var evento = Assert.IsType<ReservaConstituida>(s.Eventos[^1]);
        Assert.Equal((82_500m, Ramo.Auto, CodigoCobertura.DanosMateriales), (evento.Monto, evento.Ramo, evento.Cobertura));
    }

    [Fact]
    public void La_reserva_inicial_no_excede_la_suma_asegurada()
    {
        var poliza = AutoAmplia();
        var s = Asignado(poliza);
        Exito(s.ConstituirReserva(poliza, 900_000m, Ahora));
        Assert.Equal(350_000m, s.Reserva);
    }

    [Fact]
    public void Un_siniestro_no_elegible_no_se_reserva()
    {
        var poliza = AutoAmplia();
        var s = Asignado(poliza, ocurrencia: new DateOnly(2025, 12, 1));
        Falla(s.ConstituirReserva(poliza, 50_000m, Ahora), "sin_elegibilidad");
    }

    [Fact]
    public void Una_estimacion_que_no_supera_el_deducible_no_se_reserva()
    {
        var poliza = AutoAmplia();
        var s = Asignado(poliza);
        Falla(s.ConstituirReserva(poliza, 17_500m, Ahora), "estimacion_menor_al_deducible");
    }

    [Fact]
    public void Cada_ajuste_de_reserva_lleva_motivo_tope_y_deja_su_delta()
    {
        var poliza = AutoAmplia();
        var s = Reservado(poliza);
        Falla(s.AjustarReserva(poliza, 90_000m, "", Ahora), "motivo_requerido");
        Falla(s.AjustarReserva(poliza, 350_000.01m, "peritaje", Ahora), "reserva_invalida");
        Falla(s.AjustarReserva(poliza, 0m, "peritaje", Ahora), "reserva_invalida");
        Falla(s.AjustarReserva(poliza, 82_500m, "peritaje", Ahora), "reserva_sin_cambio");
        Exito(s.AjustarReserva(poliza, 90_000m, "peritaje del taller", Ahora));
        var evento = Assert.IsType<ReservaAjustada>(s.Eventos[^1]);
        Assert.Equal((7_500m, 90_000m), (evento.Delta, evento.Saldo));
        Assert.Equal(90_000m, s.Reserva);
    }
}

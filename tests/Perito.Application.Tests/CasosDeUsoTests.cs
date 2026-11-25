using Perito.Application.Polizas;
using Perito.Application.Siniestros;
using Perito.Domain.Ajustadores;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using static Perito.Application.Tests.Banco;

namespace Perito.Application.Tests;

/// <summary>Casos de uso del flujo con el almacén en memoria (copias por sesión, como la base).</summary>
public class CasosDeUsoTests
{
    [Fact]
    public async Task El_flujo_completo_deja_un_evento_por_hecho_en_la_bandeja_en_orden()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio);
        Exito(await b.Enviar(new TriarSiniestro(id, TipoDeSiniestro.Colision, null, "ajustador.uno")));
        Exito(await b.Enviar(new AsignarAjustador(id, await b.Ajustador(Especialidad.Auto))));
        Exito(await b.Enviar(new ConstituirReserva(id, 100_000m)));
        foreach (var dato in Catalogo.De(TipoDeSiniestro.Colision).Datos)
        {
            Exito(await b.Enviar(new RegistrarDatoRecibido(id, dato)));
        }

        Exito(await b.Enviar(new Dictaminar(id, 60_000m, [], null, "ajustador.uno")));
        Exito(await b.Enviar(new PagarSiniestro(id, "SPEI-0001", "tesoreria.uno")));

        Assert.Equal(
            [typeof(SiniestroAvisado), typeof(SiniestroTriado), typeof(AjustadorAsignado), typeof(ReservaConstituida),
                typeof(SiniestroDictaminado), typeof(PagoEmitido)],
            b.Almacen.BandejaDeSalida.Select(e => e.GetType()));
        var guardado = b.Almacen.SiniestroGuardado(id)!;
        Assert.Equal((EstadoSiniestro.Pagado, 42_500m), (guardado.Estado, guardado.Pago!.Monto));
    }

    /// <summary>El día del aviso es el de la Ciudad de México: a las 21:30 locales del 15 de junio ya es 16 en UTC.</summary>
    [Fact]
    public async Task El_dia_del_aviso_es_el_del_calendario_local_no_el_de_UTC()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        b.Reloj.Adelantar(new DateTimeOffset(2026, 6, 16, 3, 30, 0, TimeSpan.Zero) - b.Reloj.Ahora);

        var futura = await b.Enviar(new AvisarSiniestro(folio, new DateOnly(2026, 6, 16), new string('x', 30), null,
            null));
        Assert.Equal("ocurrencia_futura", futura.Error?.Codigo);
        var id = await b.Aviso(folio, new DateOnly(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 6, 15), b.Almacen.SiniestroGuardado(id)!.FechaAviso);
    }

    [Fact]
    public async Task Un_aviso_sobre_una_poliza_que_no_existe_no_se_registra()
    {
        var b = new Banco();
        var r = await b.Enviar(new AvisarSiniestro("AFV-AUT-999999", new DateOnly(2026, 6, 14), new string('x', 30),
            null, null));
        Assert.Equal("no_encontrado", r.Error?.Codigo);
        Assert.Empty(b.Almacen.BandejaDeSalida);
    }

    [Fact]
    public async Task Un_folio_no_se_emite_dos_veces_ni_sin_titular()
    {
        var b = new Banco();
        await b.PolizaAuto("AFV-AUT-000007");
        var titular = await b.Titular();
        var repetida = await b.Enviar(new EmitirPoliza("AFV-AUT-000007", Ramo.Auto, titular, new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31), []));
        Assert.Equal("folio_duplicado", repetida.Error?.Codigo);
        var huerfana = await b.Enviar(new EmitirPoliza("AFV-AUT-000008", Ramo.Auto, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31), []));
        Assert.Equal("no_encontrado", huerfana.Error?.Codigo);
    }

    /// <summary>GEN-9: la frecuencia mira los 365 días ANTERIORES a la ocurrencia; el día 366 ya no cuenta.</summary>
    [Theory]
    [InlineData(2025, 6, 14, true)]    // 365 días antes del 14-jun-2026: cuenta y con otro previo son dos
    [InlineData(2025, 6, 13, false)]   // 366 días antes: no cuenta
    public async Task La_frecuencia_cuenta_los_siniestros_de_los_doce_meses_anteriores(int a, int m, int d,
        bool indicio)
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        await b.Aviso(folio, new DateOnly(a, m, d));
        await b.Aviso(folio, new DateOnly(2026, 3, 1));
        var id = await b.Aviso(folio, new DateOnly(2026, 6, 14));
        Exito(await b.Enviar(new TriarSiniestro(id, TipoDeSiniestro.Colision, null, "ajustador.uno")));
        Assert.Equal(indicio, b.Almacen.SiniestroGuardado(id)!.Indicios.Contains(IndicioDeFraude.FrecuenciaAlta));
    }

    [Fact]
    public async Task Una_regla_que_no_procede_no_confirma_nada()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio);
        var confirmaciones = b.Almacen.Confirmaciones;
        var r = await b.Enviar(new AsignarAjustador(id, await b.Ajustador(Especialidad.Auto)));
        Assert.Equal("transicion_invalida", r.Error?.Codigo);
        Assert.Equal(confirmaciones + 1, b.Almacen.Confirmaciones);    // sólo el alta del ajustador
        Assert.Equal(EstadoSiniestro.Avisado, b.Almacen.SiniestroGuardado(id)!.Estado);
    }

    [Fact]
    public async Task Asignar_un_ajustador_inexistente_es_no_encontrado()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio);
        Exito(await b.Enviar(new TriarSiniestro(id, TipoDeSiniestro.Colision, null, "ajustador.uno")));
        Assert.Equal("no_encontrado", (await b.Enviar(new AsignarAjustador(id, Guid.NewGuid()))).Error?.Codigo);
    }

    [Fact]
    public async Task La_carga_del_ajustador_cuenta_sus_casos_abiertos_en_la_base()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var ajustador = await b.Ajustador(Especialidad.Auto);
        for (var i = 0; i < 10; i++)
        {
            var id = await b.Aviso(folio);
            Exito(await b.Enviar(new TriarSiniestro(id, TipoDeSiniestro.Colision, null, "ajustador.uno")));
            Exito(await b.Enviar(new AsignarAjustador(id, ajustador)));
        }

        var once = await b.Aviso(folio);
        Exito(await b.Enviar(new TriarSiniestro(once, TipoDeSiniestro.Colision, null, "ajustador.uno")));
        Assert.Equal("carga_completa", (await b.Enviar(new AsignarAjustador(once, ajustador))).Error?.Codigo);
    }
}

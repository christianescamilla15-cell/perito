using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Perito.Application.Privacidad;
using Perito.Application.Puertos;
using Perito.Application.Siniestros;
using Perito.Application.Triaje;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using Perito.Infrastructure.Persistencia;
using static Perito.Infrastructure.Tests.Banco;

namespace Perito.Infrastructure.Tests;

/// <summary>EF Core + Npgsql contra PostgreSQL real: esquema, ida y vuelta, concurrencia, cifrado en reposo.</summary>
[Collection(nameof(BaseDePrueba))]
public class PersistenciaTests(BaseDePrueba pg)
{
    [Fact]
    public async Task Las_migraciones_dejan_el_esquema_al_dia_con_xmin_como_columna_de_sistema()
    {
        await using var b = await pg.NuevoBancoAsync();
        await using var alcance = b.Proveedor.CreateAsyncScope();
        var db = alcance.ServiceProvider.GetRequiredService<PeritoDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var tablas = await b.TextosAsync(
            "SELECT table_name::text FROM information_schema.tables WHERE table_schema = 'perito' ORDER BY 1");
        Assert.Equal(new[] { "__historial_migraciones", "ajustadores", "asegurados", "coberturas", "mensajes_de_salida",
            "mensajes_recibidos", "movimientos_de_reserva", "polizas", "propuestas_de_triaje", "registro_de_accesos",
            "saldos_de_reserva", "siniestros" }, tablas);
        Assert.Equal(0L, await b.EscalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'perito' AND column_name = 'xmin'"));
    }

    [Fact]
    public async Task Un_siniestro_pagado_vuelve_de_la_base_igual_a_como_se_guardo()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var id = await b.ReservadoAsync(folio, await b.AjustadorAsync());
        Exito(await b.Enviar(new AjustarReserva(id, 90_000m, "peritaje del taller")));
        Exito(await b.Enviar(new Dictaminar(id, 60_000m, [], null, "ajustador.uno")));
        Exito(await b.Enviar(new PagarSiniestro(id, "SPEI-0001", "tesoreria.uno")));

        var vista = (await b.Enviar(new ConsultarSiniestro(id, "auditor", "privacidad", Finalidades.Auditoria))).Valor;
        Assert.Equal((EstadoSiniestro.Pagado, 0m, 42_500m, "AUT-2.1-a"),
            (vista.Estado, vista.Reserva, vista.Pago!.Monto, vista.Dictamen!.Clausula));
        Assert.Equal(Descripcion, vista.Descripcion);
        Assert.True(vista.TieneDetalleDeLesiones);
        Assert.Equal(new[] { "1", "2", "3" }, await b.TextosAsync(
            "SELECT secuencia::text FROM perito.movimientos_de_reserva WHERE siniestro_id = $1 ORDER BY secuencia", id));
        Assert.Equal(0m, await b.EscalarAsync<decimal>(
            "SELECT saldo FROM perito.movimientos_de_reserva WHERE siniestro_id = $1 ORDER BY secuencia DESC LIMIT 1", id));
    }

    [Fact]
    public async Task Los_identificadores_y_el_texto_libre_se_guardan_cifrados_sin_texto_claro_en_la_base()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (titular, folio) = await b.PolizaAutoAsync();
        var id = await b.AvisoAsync(folio);

        var crudo = string.Join("\n", (await b.TextosAsync("SELECT row_to_json(a)::text FROM perito.asegurados a"))
            .Concat(await b.TextosAsync("SELECT row_to_json(s)::text FROM perito.siniestros s")));
        foreach (var claro in new[] { "FEML850101AB1", "lucia.ferrer@example.com", "5541002233", "002180000000000012",
                     "calzada de los Fresnos", "Esguince" })
        {
            Assert.DoesNotContain(claro, crudo, StringComparison.OrdinalIgnoreCase);
        }

        var rfc = await b.EscalarAsync<byte[]>("SELECT rfc_cifrado FROM perito.asegurados WHERE id = $1", titular);
        Assert.Equal("k1", Privacidad.CifradorDeCampos.LlaveDe(rfc!));
        Assert.NotNull(await b.EscalarAsync<byte[]>(
            "SELECT lesiones_cifradas FROM perito.siniestros WHERE id = $1", id));
    }

    /// <summary>Leer y confirmar sin cambios no reescribe la fila: el cifrado sólo se rehace si el texto cambió.</summary>
    [Fact]
    public async Task Una_lectura_confirmada_no_reescribe_la_fila()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var id = await b.AvisoAsync(folio);
        var antes = await b.EscalarAsync<long>("SELECT xmin::text::bigint FROM perito.siniestros WHERE id = $1", id);

        await using (var alcance = b.Proveedor.CreateAsyncScope())
        {
            var siniestros = alcance.ServiceProvider.GetRequiredService<ISiniestros>();
            Assert.NotNull(await siniestros.ObtenerAsync(id, CancellationToken.None));
            await alcance.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync(CancellationToken.None);
        }

        Assert.Equal(antes, await b.EscalarAsync<long>("SELECT xmin::text::bigint FROM perito.siniestros WHERE id = $1", id));
    }

    /// <summary>GEN-12 contra la base: la propuesta se guarda como documento y el siniestro no se toca (ni su xmin ni la
    /// bandeja de salida).</summary>
    [Fact]
    public async Task La_propuesta_se_guarda_como_documento_y_no_toca_el_siniestro_ni_la_bandeja()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var id = await b.AvisoAsync(folio);
        var version = await b.EscalarAsync<long>("SELECT xmin::text::bigint FROM perito.siniestros WHERE id = $1", id);
        var mensajes = await b.EscalarAsync<long>("SELECT count(*) FROM perito.mensajes_de_salida");

        var propuesta = (await b.Enviar(new ProponerTriaje(id, "ajustador.uno"))).Valor;

        Assert.Equal(version, await b.EscalarAsync<long>("SELECT xmin::text::bigint FROM perito.siniestros WHERE id = $1", id));
        Assert.Equal(mensajes, await b.EscalarAsync<long>("SELECT count(*) FROM perito.mensajes_de_salida"));
        Assert.Equal("AUT-2.1-a", await b.EscalarAsync<string>(
            "SELECT documento->>'clausula' FROM perito.propuestas_de_triaje WHERE id = $1", propuesta.Id));
        var leida = await b.Enviar(new ConsultarPropuesta(propuesta.Id));
        Assert.Equal(JsonSerializer.Serialize(propuesta), JsonSerializer.Serialize(leida));
    }

    [Fact]
    public async Task Dos_cambios_concurrentes_al_mismo_siniestro_uno_gana_y_el_otro_es_conflicto_sin_dejar_eventos()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var id = await b.AvisoAsync(folio);
        await using var uno = b.Proveedor.CreateAsyncScope();
        await using var dos = b.Proveedor.CreateAsyncScope();
        var s1 = (await uno.ServiceProvider.GetRequiredService<ISiniestros>().ObtenerAsync(id, CancellationToken.None))!;
        var s2 = (await dos.ServiceProvider.GetRequiredService<ISiniestros>().ObtenerAsync(id, CancellationToken.None))!;
        var poliza = (await uno.ServiceProvider.GetRequiredService<IPolizas>().ObtenerAsync(s1.PolizaId, CancellationToken.None))!;
        Exito(s1.Triar(TipoDeSiniestro.Colision, poliza, 0, "ajustador.uno", null, b.Reloj.Ahora));
        Exito(s2.Triar(TipoDeSiniestro.Vuelco, poliza, 0, "ajustador.dos", null, b.Reloj.Ahora));

        await uno.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ConflictoDeConcurrencia>(() =>
            dos.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync(CancellationToken.None));

        Assert.Equal("Colision", await b.EscalarAsync<string>("SELECT tipo FROM perito.siniestros WHERE id = $1", id));
        Assert.Equal(1L, await b.EscalarAsync<long>(
            "SELECT count(*) FROM perito.mensajes_de_salida WHERE tipo = 'SiniestroTriado' AND agregado = $1", id));
    }

    [Fact]
    public async Task El_folio_es_unico_en_la_base_aunque_dos_altas_pasen_la_revision_a_la_vez()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (titular, _) = await b.PolizaAutoAsync();
        Poliza Nueva() => Poliza.Emitir(Guid.NewGuid(), "AFV-AUT-DUPLICADO", Ramo.Auto, titular, new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31),
            [new(CodigoCobertura.RoboTotal, 1m, new Deducible(TipoDeDeducible.Fijo, 0m), 0m)]).Valor;
        await using var uno = b.Proveedor.CreateAsyncScope();
        await using var dos = b.Proveedor.CreateAsyncScope();
        uno.ServiceProvider.GetRequiredService<IPolizas>().Agregar(Nueva());
        dos.ServiceProvider.GetRequiredService<IPolizas>().Agregar(Nueva());

        await uno.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync(CancellationToken.None);
        await Assert.ThrowsAsync<ConflictoDeConcurrencia>(() =>
            dos.ServiceProvider.GetRequiredService<IUnidadDeTrabajo>().ConfirmarAsync(CancellationToken.None));
    }

    /// <summary>GEN-9 en SQL: la ventana de frecuencia es inclusiva en los dos extremos y excluye al propio siniestro.</summary>
    [Fact]
    public async Task La_ventana_de_frecuencia_se_traduce_a_SQL_con_sus_extremos()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var propio = await b.AvisoAsync(folio, new DateOnly(2026, 6, 14));
        await b.AvisoAsync(folio, new DateOnly(2025, 6, 14));
        await b.AvisoAsync(folio, new DateOnly(2026, 6, 13));
        await b.AvisoAsync(folio, new DateOnly(2025, 6, 13));
        await using var alcance = b.Proveedor.CreateAsyncScope();
        var poliza = await alcance.ServiceProvider.GetRequiredService<IPolizas>().ObtenerPorFolioAsync(folio, default);
        var n = await alcance.ServiceProvider.GetRequiredService<ISiniestros>().ContarDeLaPolizaAsync(poliza!.Id, propio,
            new DateOnly(2025, 6, 14), new DateOnly(2026, 6, 13), default);
        Assert.Equal(2, n);
    }

    [Fact]
    public async Task Consultar_un_siniestro_deja_su_registro_de_acceso_sin_datos_personales()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (titular, folio) = await b.PolizaAutoAsync();
        var id = await b.AvisoAsync(folio);
        Exito(await b.Enviar(new ConsultarSiniestro(id, "ajustador.uno", "ajustador", Finalidades.AtencionDelSiniestro)));

        var fila = Assert.Single(await b.TextosAsync(
            "SELECT row_to_json(r)::text FROM perito.registro_de_accesos r"));
        Assert.Contains(titular.ToString(), fila, StringComparison.Ordinal);
        Assert.Contains("atencion_del_siniestro", fila, StringComparison.Ordinal);
        Assert.DoesNotContain("Lucía", fila, StringComparison.Ordinal);
        var columnas = await b.TextosAsync("SELECT column_name::text FROM information_schema.columns " +
                                           "WHERE table_schema = 'perito' AND table_name = 'registro_de_accesos' ORDER BY 1");
        Assert.Equal(new[] { "fecha", "finalidad", "id", "recurso", "recurso_id", "rol", "titular_id", "usuario" }, columnas);
    }

    [Fact]
    public async Task Anonimizar_no_deja_rastro_de_los_datos_del_titular_en_ninguna_tabla()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (titular, folio) = await b.PolizaAutoAsync();
        var id = await b.ReservadoAsync(folio, await b.AjustadorAsync());
        Exito(await b.Enviar(new Dictaminar(id, 60_000m, [], null, "ajustador.uno")));
        Exito(await b.Enviar(new PagarSiniestro(id, "SPEI-0002", "tesoreria.uno")));
        await b.Enviar(new ProponerTriaje(id, "ajustador.uno"));      // ya triado: no genera propuesta

        Exito(await b.Enviar(new AnonimizarAsegurado(titular, "privacidad.uno", "privacidad")));

        var todo = string.Join("\n", await b.TextosAsync(
            """
            SELECT string_agg(t::text, E'\n') FROM (
              SELECT row_to_json(x)::text AS t FROM perito.asegurados x UNION ALL
              SELECT row_to_json(x)::text FROM perito.siniestros x UNION ALL
              SELECT row_to_json(x)::text FROM perito.propuestas_de_triaje x UNION ALL
              SELECT row_to_json(x)::text FROM perito.mensajes_de_salida x UNION ALL
              SELECT row_to_json(x)::text FROM perito.registro_de_accesos x) todo
            """));
        foreach (var dato in new[] { "Lucía", "Ferrer", "FEML", "lucia.ferrer", "41002233", "Fresnos" })
        {
            Assert.DoesNotContain(dato, todo, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(0L, await b.EscalarAsync<long>(
            "SELECT count(*) FROM perito.asegurados WHERE rfc_cifrado IS NOT NULL OR correo_cifrado IS NOT NULL " +
            "OR telefono_cifrado IS NOT NULL OR clabe_cifrada IS NOT NULL"));
        Assert.Equal(0L, await b.EscalarAsync<long>(
            "SELECT count(*) FROM perito.siniestros WHERE descripcion_cifrada IS NOT NULL OR lesiones_cifradas IS NOT NULL"));
        Assert.Equal(42_500m, await b.EscalarAsync<decimal>("SELECT pago_monto FROM perito.siniestros WHERE id = $1", id));
    }
}

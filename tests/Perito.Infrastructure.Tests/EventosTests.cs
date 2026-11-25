using Perito.Application.Lecturas;
using Perito.Application.Puertos;
using Perito.Application.Siniestros;
using Perito.Domain.Comun;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using Perito.Infrastructure.Eventos;
using static Perito.Infrastructure.Tests.Banco;

namespace Perito.Infrastructure.Tests;

/// <summary>Consumidor de prueba que siempre falla (para reintentos y mensajes muertos).</summary>
public sealed class ConsumidorQueFalla : IConsumidorDeEventos
{
    public int Llamadas { get; private set; }

    public string Nombre => "falla";

    public bool Atiende(IEventoDeDominio evento) => evento is SiniestroAvisado;

    public Task ConsumirAsync(Guid mensajeId, IEventoDeDominio evento, CancellationToken ct)
    {
        Llamadas++;
        throw new InvalidOperationException("el destino no responde");
    }
}

/// <summary>
/// Bandeja de salida transaccional y consumidor idempotente contra PostgreSQL real: entrega al menos una vez, sin
/// duplicar efectos, con reintentos, mensajes muertos y despachadores concurrentes.
/// </summary>
[Collection(nameof(BaseDePrueba))]
public class EventosTests(BaseDePrueba pg)
{
    [Fact]
    public async Task Cada_cambio_deja_su_evento_en_la_bandeja_en_la_misma_confirmacion()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var id = await b.ReservadoAsync(folio, await b.AjustadorAsync());
        var tipos = await b.TextosAsync(
            "SELECT tipo FROM perito.mensajes_de_salida WHERE agregado = $1 ORDER BY ocurrido_en, secuencia", id);
        Assert.Equal(new[] { "SiniestroAvisado", "SiniestroTriado", "AjustadorAsignado", "ReservaConstituida" }, tipos);
    }

    [Fact]
    public async Task Los_eventos_de_la_bandeja_no_llevan_datos_personales()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var id = await b.ReservadoAsync(folio, await b.AjustadorAsync());
        Exito(await b.Enviar(new Dictaminar(id, 60_000m, [], null, "ajustador.uno")));
        Exito(await b.Enviar(new PagarSiniestro(id, "SPEI-0003", "tesoreria.uno")));

        var contenidos = string.Join("\n", await b.TextosAsync("SELECT contenido::text FROM perito.mensajes_de_salida"));
        Assert.Contains("PagoEmitido", string.Join(",", await b.TextosAsync("SELECT tipo FROM perito.mensajes_de_salida")),
            StringComparison.Ordinal);
        foreach (var dato in new[] { "Lucía", "Ferrer", "FEML", "lucia.ferrer", "41002233", "Fresnos", "Esguince",
                     "ajustador.uno", "tesoreria.uno" })
        {
            Assert.DoesNotContain(dato, contenidos, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task El_despachador_entrega_a_la_proyeccion_y_marca_cada_mensaje_procesado()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        await b.ReservadoAsync(folio, await b.AjustadorAsync());

        await b.DespacharTodoAsync();

        var tablero = await b.Enviar(new ConsultarTableroDeReservas());
        Assert.Equal((Ramo.Auto, 82_500m, 1), Assert.Single(tablero) is var t ? (t.Ramo, t.ReservaVigente, t.SiniestrosConReserva) : default);
        Assert.Equal(0L, await b.EscalarAsync<long>(
            "SELECT count(*) FROM perito.mensajes_de_salida WHERE procesado_en IS NULL"));
    }

    /// <summary>
    /// Caída entre consumir y marcar: el consumidor ya aplicó el mensaje y el despachador lo vuelve a entregar. La bandeja
    /// de entrada lo reconoce y el saldo no se duplica.
    /// </summary>
    [Fact]
    public async Task Un_mensaje_entregado_dos_veces_se_aplica_una_sola_vez()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var id = await b.ReservadoAsync(folio, await b.AjustadorAsync());
        var mensaje = await b.EscalarAsync<Guid>(
            "SELECT id FROM perito.mensajes_de_salida WHERE tipo = 'ReservaConstituida' AND agregado = $1", id);
        var contenido = await b.EscalarAsync<string>("SELECT contenido::text FROM perito.mensajes_de_salida WHERE id = $1",
            mensaje);
        var proyeccion = new ProyeccionDeReservas(
            Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Persistencia.PeritoDbContext>>(b.Proveedor),
            b.Reloj);
        await proyeccion.ConsumirAsync(mensaje, SerializadorDeEventos.Leer("ReservaConstituida", contenido!), default);

        await b.DespacharTodoAsync();
        await proyeccion.ConsumirAsync(mensaje, SerializadorDeEventos.Leer("ReservaConstituida", contenido!), default);

        Assert.Equal(82_500m, (await b.Enviar(new ConsultarTableroDeReservas())).Single().ReservaVigente);
        Assert.Equal(1L, await b.EscalarAsync<long>(
            "SELECT count(*) FROM perito.mensajes_recibidos WHERE mensaje_id = $1", mensaje));
    }

    [Fact]
    public async Task Un_consumidor_que_falla_se_reintenta_con_espera_creciente_y_termina_muerto_con_su_error()
    {
        var falla = new ConsumidorQueFalla();
        await using var b = await pg.NuevoBancoAsync(falla);
        var (_, folio) = await b.PolizaAutoAsync();
        var id = await b.AvisoAsync(folio);
        const string Sql = "SELECT {0} FROM perito.mensajes_de_salida WHERE agregado = $1 AND tipo = 'SiniestroAvisado'";

        await b.Despachador.ProcesarLoteAsync(default);                      // intento 1: espera 10 s
        await b.Despachador.ProcesarLoteAsync(default);                      // aún no toca: no se reintenta
        Assert.Equal(1, falla.Llamadas);
        b.Reloj.Ahora += TimeSpan.FromSeconds(10);
        await b.Despachador.ProcesarLoteAsync(default);                      // intento 2: espera 20 s
        b.Reloj.Ahora += TimeSpan.FromSeconds(19);
        await b.Despachador.ProcesarLoteAsync(default);
        Assert.Equal(2, falla.Llamadas);
        b.Reloj.Ahora += TimeSpan.FromSeconds(1);
        await b.Despachador.ProcesarLoteAsync(default);                      // intento 3 = máximo: muerto

        Assert.Equal(3, falla.Llamadas);
        Assert.True(await b.EscalarAsync<bool>(string.Format(Sql, "muerto"), id));
        Assert.Equal("InvalidOperationException: el destino no responde",
            await b.EscalarAsync<string>(string.Format(Sql, "ultimo_error"), id));
        b.Reloj.Ahora += TimeSpan.FromHours(1);
        await b.Despachador.ProcesarLoteAsync(default);
        Assert.Equal(3, falla.Llamadas);                                     // un mensaje muerto ya no se toma
    }

    /// <summary>
    /// Dos despachadores a la vez sobre 20 siniestros: FOR UPDATE SKIP LOCKED reparte los mensajes sin repetirlos y el
    /// saldo proyectado cuadra con la suma de las reservas vigentes en la tabla de siniestros.
    /// </summary>
    [Fact]
    public async Task Dos_despachadores_a_la_vez_no_procesan_un_mensaje_dos_veces_y_la_proyeccion_cuadra()
    {
        await using var b = await pg.NuevoBancoAsync();
        var (_, folio) = await b.PolizaAutoAsync();
        var ajustadores = new[] { await b.AjustadorAsync(), await b.AjustadorAsync() };
        var ids = new List<Guid>();
        for (var i = 0; i < 20; i++)
        {
            ids.Add(await b.ReservadoAsync(folio, ajustadores[i % 2], 50_000m + i * 1_000m));
        }

        Exito(await b.Enviar(new AjustarReserva(ids[0], 60_000m, "peritaje del taller")));
        Exito(await b.Enviar(new Dictaminar(ids[1], 40_000m, [], null, "ajustador.uno")));
        Exito(await b.Enviar(new PagarSiniestro(ids[1], "SPEI-0004", "tesoreria.uno")));
        Exito(await b.Enviar(new Dictaminar(ids[2], 40_000m, [CodigoExclusion.Competencias], null, "ajustador.uno")));
        Exito(await b.Enviar(new RechazarSiniestro(ids[2])));

        var a = Task.Run(b.DespacharTodoAsync);
        var c = Task.Run(b.DespacharTodoAsync);
        await Task.WhenAll(a, c);

        var relevantes = await b.EscalarAsync<long>("SELECT count(*) FROM perito.mensajes_de_salida WHERE tipo IN " +
                                                    "('ReservaConstituida','ReservaAjustada','PagoEmitido','SiniestroRechazado')");
        Assert.Equal(relevantes, await b.EscalarAsync<long>("SELECT count(*) FROM perito.mensajes_recibidos"));
        var esperado = await b.EscalarAsync<decimal>(
            """
            SELECT coalesce(sum(m.saldo), 0) FROM perito.movimientos_de_reserva m
            WHERE m.secuencia = (SELECT max(secuencia) FROM perito.movimientos_de_reserva x WHERE x.siniestro_id = m.siniestro_id)
            """);
        var saldo = (await b.Enviar(new ConsultarTableroDeReservas())).Single();
        Assert.Equal(esperado, saldo.ReservaVigente);
        Assert.Equal(18, saldo.SiniestrosConReserva);
    }
}

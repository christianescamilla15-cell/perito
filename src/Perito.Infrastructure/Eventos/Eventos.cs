using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Perito.Application.Puertos;
using Perito.Domain.Comun;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using Perito.Infrastructure.Persistencia;

namespace Perito.Infrastructure.Eventos;

/// <summary>Eventos de dominio ↔ JSON con su tipo (el nombre de la clase, estable).</summary>
public static class SerializadorDeEventos
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Dictionary<string, Type> Tipos = typeof(IEventoDeDominio).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEventoDeDominio).IsAssignableFrom(t))
        .ToDictionary(t => t.Name, StringComparer.Ordinal);

    /// <summary>Tipo y contenido JSON de un evento.</summary>
    public static (string Tipo, string Contenido) Escribir(IEventoDeDominio evento) =>
        (evento.GetType().Name, JsonSerializer.Serialize(evento, evento.GetType(), Json));

    /// <summary>El evento a partir de su tipo y contenido.</summary>
    public static IEventoDeDominio Leer(string tipo, string contenido) =>
        Tipos.TryGetValue(tipo, out var t)
            ? (IEventoDeDominio)JsonSerializer.Deserialize(contenido, t, Json)!
            : throw new InvalidOperationException($"Tipo de evento desconocido: {tipo}");
}

/// <summary>Opciones del despachador de la bandeja de salida.</summary>
public sealed record OpcionesDeSalida
{
    /// <summary>Mensajes por lote.</summary>
    public int TamanoDeLote { get; init; } = 50;

    /// <summary>Intentos antes de marcar un mensaje como muerto.</summary>
    public int IntentosMaximos { get; init; } = 5;

    /// <summary>Espera base del reintento (se duplica con cada intento).</summary>
    public TimeSpan EsperaBase { get; init; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Despacha la bandeja de salida: toma un lote con FOR UPDATE SKIP LOCKED (varios despachadores no se pisan), entrega
/// cada mensaje a sus consumidores y lo marca procesado. Si un consumidor falla, el mensaje se reintenta con espera
/// creciente y, al agotar los intentos, queda muerto con su último error (sin datos personales: los eventos no los
/// llevan). Entrega «al menos una vez»: los consumidores son idempotentes.
/// </summary>
public sealed class DespachadorDeSalida(
    IDbContextFactory<PeritoDbContext> fabrica, IEnumerable<IConsumidorDeEventos> consumidores, IClock reloj,
    OpcionesDeSalida opciones, ILogger<DespachadorDeSalida> bitacora)
{
    /// <summary>Procesa un lote; devuelve cuántos mensajes tomó.</summary>
    public async Task<int> ProcesarLoteAsync(CancellationToken ct)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ahora = reloj.Ahora;
        var lote = await db.Salida.FromSql(
                $"""
                 SELECT * FROM perito.mensajes_de_salida
                 WHERE procesado_en IS NULL AND NOT muerto AND (siguiente_intento IS NULL OR siguiente_intento <= {ahora})
                 ORDER BY ocurrido_en, secuencia
                 LIMIT {opciones.TamanoDeLote}
                 FOR UPDATE SKIP LOCKED
                 """)
            .ToListAsync(ct);
        foreach (var mensaje in lote)
        {
            try
            {
                var evento = SerializadorDeEventos.Leer(mensaje.Tipo, mensaje.Contenido);
                foreach (var consumidor in consumidores.Where(c => c.Atiende(evento)))
                {
                    await consumidor.ConsumirAsync(mensaje.Id, evento, ct);
                }

                mensaje.ProcesadoEn = ahora;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                mensaje.Intentos++;
                var error = $"{e.GetType().Name}: {e.Message}";
                mensaje.UltimoError = error.Length > 500 ? error[..500] : error;
                mensaje.Muerto = mensaje.Intentos >= opciones.IntentosMaximos;
                mensaje.SiguienteIntento = mensaje.Muerto
                    ? null
                    : ahora + opciones.EsperaBase * Math.Pow(2, mensaje.Intentos - 1);
                bitacora.LogWarning("Mensaje {Id} ({Tipo}) falló el intento {Intento}: {Error}", mensaje.Id,
                    mensaje.Tipo, mensaje.Intentos, e.GetType().Name);
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return lote.Count;
    }
}

/// <summary>
/// Consumidor idempotente: mantiene el saldo de reservas por ramo. En UNA transacción inserta el id del mensaje en su
/// bandeja de entrada (si ya estaba, no hace nada) y aplica el delta. Los deltas son sumas, así que el orden de
/// llegada no altera el resultado.
/// </summary>
public sealed class ProyeccionDeReservas(IDbContextFactory<PeritoDbContext> fabrica, IClock reloj)
    : IConsumidorDeEventos
{
    /// <inheritdoc />
    public string Nombre => "proyeccion-de-reservas";

    /// <inheritdoc />
    public bool Atiende(IEventoDeDominio evento) => Delta(evento) is not null;

    /// <inheritdoc />
    public async Task ConsumirAsync(Guid mensajeId, IEventoDeDominio evento, CancellationToken ct)
    {
        if (Delta(evento) is not { } delta)
        {
            return;
        }

        await using var db = await fabrica.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var nuevo = await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO perito.mensajes_recibidos (consumidor, mensaje_id, procesado_en)
             VALUES ({Nombre}, {mensajeId}, {reloj.Ahora}) ON CONFLICT DO NOTHING
             """, ct);
        if (nuevo == 0)
        {
            return;     // ya aplicado: la transacción se descarta sin cambios
        }

        var ramo = delta.Ramo.ToString();
        await db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO perito.saldos_de_reserva (ramo, reserva_vigente, siniestros_con_reserva)
             VALUES ({ramo}, {delta.Monto}, {delta.Conteo})
             ON CONFLICT (ramo) DO UPDATE SET
               reserva_vigente = saldos_de_reserva.reserva_vigente + EXCLUDED.reserva_vigente,
               siniestros_con_reserva = saldos_de_reserva.siniestros_con_reserva + EXCLUDED.siniestros_con_reserva
             """, ct);
        await tx.CommitAsync(ct);
    }

    private static (Ramo Ramo, decimal Monto, int Conteo)? Delta(IEventoDeDominio evento) => evento switch
    {
        ReservaConstituida r => (r.Ramo, r.Monto, 1),
        ReservaAjustada a => (a.Ramo, a.Delta, 0),
        PagoEmitido { ReservaLiberada: not 0 } p => (p.Ramo, -p.ReservaLiberada, -1),
        SiniestroRechazado { ReservaLiberada: not 0 } s => (s.Ramo, -s.ReservaLiberada, -1),
        _ => null,
    };
}

/// <summary>Lectura del tablero desde la proyección.</summary>
public sealed class TableroDeReservasEf(IDbContextFactory<PeritoDbContext> fabrica) : ITableroDeReservas
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SaldoDeReservaPorRamo>> LeerAsync(CancellationToken ct)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        var filas = await db.Saldos.AsNoTracking().OrderBy(s => s.Ramo).ToListAsync(ct);
        return filas.Select(s => new SaldoDeReservaPorRamo(Enum.Parse<Ramo>(s.Ramo), s.ReservaVigente,
            s.SiniestrosConReserva)).ToList();
    }
}

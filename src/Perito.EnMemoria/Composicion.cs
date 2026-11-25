using Microsoft.Extensions.DependencyInjection;
using Perito.Application.Abstracciones;
using Perito.Application.Puertos;
using Perito.Domain.Comun;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;

namespace Perito.EnMemoria;

/// <summary>Composición en memoria: los mismos manejadores que la API, con el almacén en memoria.</summary>
public static class Composicion
{
    /// <summary>Registra almacén, sesión por alcance, reloj, mediador y manejadores. El asistente lo pone quien llama.</summary>
    public static IServiceCollection AgregarPeritoEnMemoria(this IServiceCollection servicios, AlmacenEnMemoria almacen,
        IClock reloj)
    {
        servicios.AddSingleton(almacen);
        servicios.AddSingleton(reloj);
        servicios.AddScoped<SesionEnMemoria>();
        servicios.AddScoped<IAsegurados>(p => p.GetRequiredService<SesionEnMemoria>());
        servicios.AddScoped<IPolizas>(p => p.GetRequiredService<SesionEnMemoria>());
        servicios.AddScoped<IAjustadores>(p => p.GetRequiredService<SesionEnMemoria>());
        servicios.AddScoped<ISiniestros>(p => p.GetRequiredService<SesionEnMemoria>());
        servicios.AddScoped<IPropuestasDeTriaje>(p => p.GetRequiredService<SesionEnMemoria>());
        servicios.AddScoped<IUnidadDeTrabajo>(p => p.GetRequiredService<SesionEnMemoria>());
        servicios.AddSingleton<IRegistroDeAccesos, RegistroDeAccesosEnMemoria>();
        servicios.AddSingleton<ITableroDeReservas, TableroEnMemoria>();
        servicios.AddScoped<IMediador, Mediador>();
        foreach (var (servicio, implementacion) in Manejadores.DeLaAplicacion())
        {
            servicios.AddScoped(servicio, implementacion);
        }

        return servicios;
    }
}

/// <summary>Tablero calculado directamente de los siniestros guardados (sin consumidor de eventos).</summary>
public sealed class TableroEnMemoria(AlmacenEnMemoria almacen) : ITableroDeReservas
{
    /// <inheritdoc />
    public Task<IReadOnlyList<SaldoDeReservaPorRamo>> LeerAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SaldoDeReservaPorRamo>>(almacen.Leer(() => almacen.Siniestros.Values
            .Select(Siniestro.Rehidratar).Where(s => s.Reserva != 0)
            .GroupBy(s => s.Ramo).OrderBy(g => g.Key)
            .Select(g => new SaldoDeReservaPorRamo(g.Key, g.Sum(s => s.Reserva), g.Count())).ToList()));
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Perito.Application.Abstracciones;
using Perito.Application.Puertos;
using Perito.Domain.Comun;
using Perito.Infrastructure.Eventos;
using Perito.Infrastructure.Persistencia;
using Perito.Infrastructure.Privacidad;
using Perito.Infrastructure.Reloj;

namespace Perito.Infrastructure;

/// <summary>Composición de la infraestructura: PostgreSQL, almacén y unidad de trabajo por solicitud, cifrado, registro
/// de accesos, bandeja de salida y su consumidor, reloj del sistema, mediador y manejadores.</summary>
public static class Servicios
{
    /// <summary>Registra todo lo de la infraestructura.</summary>
    public static IServiceCollection AgregarPerito(this IServiceCollection servicios, string cadenaDeConexion,
        OpcionesDeCifrado cifrado, OpcionesDeSalida? salida = null)
    {
        servicios.AddDbContextFactory<PeritoDbContext>(o => o.UseNpgsql(cadenaDeConexion,
            n => n.MigrationsHistoryTable("__historial_migraciones", PeritoDbContext.Esquema)));
        servicios.AddScoped(p => p.GetRequiredService<IDbContextFactory<PeritoDbContext>>().CreateDbContext());
        servicios.AddSingleton(new CifradorDeCampos(cifrado));
        servicios.AddScoped<AlmacenEf>();
        servicios.AddScoped<IAsegurados>(p => p.GetRequiredService<AlmacenEf>());
        servicios.AddScoped<IPolizas>(p => p.GetRequiredService<AlmacenEf>());
        servicios.AddScoped<IAjustadores>(p => p.GetRequiredService<AlmacenEf>());
        servicios.AddScoped<ISiniestros>(p => p.GetRequiredService<AlmacenEf>());
        servicios.AddScoped<IPropuestasDeTriaje>(p => p.GetRequiredService<AlmacenEf>());
        servicios.AddScoped<IUnidadDeTrabajo>(p => p.GetRequiredService<AlmacenEf>());
        servicios.AddSingleton<IRegistroDeAccesos, RegistroDeAccesosEf>();
        servicios.AddSingleton<ITableroDeReservas, TableroDeReservasEf>();
        servicios.AddSingleton<IConsumidorDeEventos, ProyeccionDeReservas>();
        servicios.AddSingleton(salida ?? new OpcionesDeSalida());
        servicios.AddSingleton<DespachadorDeSalida>();
        servicios.AddSingleton<IClock, SystemClock>();
        servicios.AddScoped<IMediador, Mediador>();
        foreach (var (servicio, implementacion) in Manejadores.DeLaAplicacion())
        {
            servicios.AddScoped(servicio, implementacion);
        }

        return servicios;
    }

    /// <summary>Aplica las migraciones pendientes sin exponer el DbContext a quien compone.</summary>
    public static async Task AplicarMigracionesAsync(this IServiceProvider servicios, CancellationToken ct = default)
    {
        await using var alcance = servicios.CreateAsyncScope();
        await alcance.ServiceProvider.GetRequiredService<PeritoDbContext>().Database.MigrateAsync(ct);
    }
}

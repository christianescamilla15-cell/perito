namespace Perito.Application.Abstracciones;

/// <summary>Una solicitud (consulta o comando) con su respuesta.</summary>
public interface ISolicitud<TRespuesta>;

/// <summary>Marca de los comandos: las solicitudes que pueden cambiar estado. El asistente no puede enviar ninguna
/// (prueba de arquitectura).</summary>
public interface IComando<TRespuesta> : ISolicitud<TRespuesta>;

/// <summary>Marca de las consultas: sólo leen.</summary>
public interface IConsulta<TRespuesta> : ISolicitud<TRespuesta>;

/// <summary>Maneja una solicitud.</summary>
public interface IManejador<in TSolicitud, TRespuesta>
    where TSolicitud : ISolicitud<TRespuesta>
{
    /// <summary>Atiende la solicitud.</summary>
    Task<TRespuesta> ManejarAsync(TSolicitud solicitud, CancellationToken ct);
}

/// <summary>Envía una solicitud a su manejador.</summary>
public interface IMediador
{
    /// <summary>Resuelve el manejador de la solicitud y lo llama.</summary>
    Task<TRespuesta> EnviarAsync<TRespuesta>(ISolicitud<TRespuesta> solicitud, CancellationToken ct = default);
}

/// <summary>
/// Mediador propio y delgado: resuelve del contenedor el manejador de la solicitud y lo llama. Sin tuberías; si falta
/// el manejador, falla al primer uso con el nombre de la solicitud.
/// </summary>
public sealed class Mediador(IServiceProvider servicios) : IMediador
{
    /// <inheritdoc />
    public Task<TRespuesta> EnviarAsync<TRespuesta>(ISolicitud<TRespuesta> solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var tipo = typeof(Envoltura<,>).MakeGenericType(solicitud.GetType(), typeof(TRespuesta));
        var envoltura = (IEnvoltura<TRespuesta>)Activator.CreateInstance(tipo)!;
        return envoltura.Enviar(solicitud, servicios, ct);
    }

    private interface IEnvoltura<TRespuesta>
    {
        Task<TRespuesta> Enviar(ISolicitud<TRespuesta> solicitud, IServiceProvider servicios, CancellationToken ct);
    }

    private sealed class Envoltura<TSolicitud, TRespuesta> : IEnvoltura<TRespuesta>
        where TSolicitud : ISolicitud<TRespuesta>
    {
        public Task<TRespuesta> Enviar(ISolicitud<TRespuesta> solicitud, IServiceProvider servicios,
            CancellationToken ct)
        {
            var manejador = servicios.GetService(typeof(IManejador<TSolicitud, TRespuesta>))
                as IManejador<TSolicitud, TRespuesta>
                ?? throw new InvalidOperationException($"sin manejador para {typeof(TSolicitud).Name}");
            return manejador.ManejarAsync((TSolicitud)solicitud, ct);
        }
    }
}

/// <summary>Los manejadores de la aplicación, para que cada composición (API, evaluación, pruebas) los registre igual
/// sin que Application dependa de un contenedor.</summary>
public static class Manejadores
{
    /// <summary>Pares (interfaz IManejador cerrada, implementación).</summary>
    public static IReadOnlyList<(Type Servicio, Type Implementacion)> DeLaAplicacion() =>
        typeof(Mediador).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IManejador<,>))
                .Select(i => (i, t)))
            .ToList();
}

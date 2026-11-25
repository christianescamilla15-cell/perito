using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Puertos;
using Perito.Domain.Comun;

namespace Perito.Application.Privacidad;

/// <summary>Para qué se consultan datos personales (principio de finalidad): toda lectura declara una de éstas.</summary>
public static class Finalidades
{
    /// <summary>Atender el siniestro (ajuste, dictamen, pago).</summary>
    public const string AtencionDelSiniestro = "atencion_del_siniestro";

    /// <summary>Atender una solicitud del titular (acceso, rectificación, cancelación u oposición).</summary>
    public const string DerechosDelTitular = "derechos_del_titular";

    /// <summary>Auditoría interna.</summary>
    public const string Auditoria = "auditoria";

    /// <summary>Anonimización a solicitud del titular.</summary>
    public const string Anonimizacion = "anonimizacion";

    private static readonly HashSet<string> Validas = [AtencionDelSiniestro, DerechosDelTitular, Auditoria];

    /// <summary>El error cuando la finalidad falta o no está en la lista.</summary>
    public static readonly Error ErrorDeFinalidad = new("finalidad_invalida",
        $"Declare la finalidad: {string.Join(", ", Validas.Order())}.");

    /// <summary>Si la finalidad declarada está permitida para consultar.</summary>
    public static bool EsValida(string? finalidad) => finalidad is not null && Validas.Contains(finalidad);
}

/// <summary>Los datos de un titular tal como se le muestran a quien tiene derecho a verlos.</summary>
public sealed record AseguradoVista(
    Guid Id, string Nombre, string? Rfc, string? Correo, string? Telefono, string? Clabe, DateTimeOffset? AnonimizadoEn);

/// <summary>Consulta los datos de un titular con su finalidad (queda registrada).</summary>
public sealed record ConsultarAsegurado(Guid AseguradoId, string Usuario, string Rol, string Finalidad)
    : IConsulta<Resultado<AseguradoVista>>;

/// <summary>Anonimiza a un titular a su solicitud.</summary>
public sealed record AnonimizarAsegurado(Guid AseguradoId, string Usuario, string Rol) : IComando<Resultado>;

/// <summary>Quién consultó los datos de un titular (derecho de acceso).</summary>
public sealed record ConsultarAccesos(Guid AseguradoId) : IConsulta<IReadOnlyList<AccesoADatosPersonales>>;

/// <summary>Manejadores de privacidad.</summary>
public sealed class ManejadoresDePrivacidad(
    IAsegurados asegurados, ISiniestros siniestros, IRegistroDeAccesos accesos, IUnidadDeTrabajo unidad,
    IClock reloj)
    : IManejador<ConsultarAsegurado, Resultado<AseguradoVista>>,
        IManejador<AnonimizarAsegurado, Resultado>,
        IManejador<ConsultarAccesos, IReadOnlyList<AccesoADatosPersonales>>
{
    /// <inheritdoc />
    public async Task<Resultado<AseguradoVista>> ManejarAsync(ConsultarAsegurado s, CancellationToken ct)
    {
        if (!Finalidades.EsValida(s.Finalidad))
        {
            return Resultado<AseguradoVista>.Fallo(Finalidades.ErrorDeFinalidad);
        }

        if (await asegurados.ObtenerAsync(s.AseguradoId, ct) is not { } a)
        {
            return Resultado<AseguradoVista>.Fallo(Errores.NoEncontrado("el titular"));
        }

        await accesos.RegistrarAsync(new AccesoADatosPersonales(Guid.NewGuid(), reloj.Ahora, s.Usuario, s.Rol,
            "asegurado", a.Id, a.Id, s.Finalidad), ct);
        return Resultado<AseguradoVista>.Exito(new AseguradoVista(a.Id, a.Nombre, a.Rfc, a.Correo, a.Telefono, a.Clabe,
            a.AnonimizadoEn));
    }

    /// <inheritdoc />
    public async Task<Resultado> ManejarAsync(AnonimizarAsegurado s, CancellationToken ct)
    {
        if (await asegurados.ObtenerAsync(s.AseguradoId, ct) is not { } a)
        {
            return Resultado.Fallo(Errores.NoEncontrado("el titular"));
        }

        var suyos = await siniestros.DelAseguradoAsync(a.Id, ct);
        var ahora = reloj.Ahora;
        var r = a.Anonimizar(suyos.Count(x => x.EstaAbierto), ahora);
        if (!r.EsExito)
        {
            return r;
        }

        // El texto libre de sus siniestros puede traer datos personales: se borra. Montos, fechas y estados se
        // conservan para cumplir la conservación de registros financieros.
        foreach (var siniestro in suyos)
        {
            siniestro.BorrarTextoLibre();
        }

        await unidad.ConfirmarAsync(ct);
        await accesos.RegistrarAsync(new AccesoADatosPersonales(Guid.NewGuid(), ahora, s.Usuario, s.Rol, "asegurado",
            a.Id, a.Id, Finalidades.Anonimizacion), ct);
        return Resultado.Exito();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccesoADatosPersonales>> ManejarAsync(ConsultarAccesos s, CancellationToken ct) =>
        accesos.DelTitularAsync(s.AseguradoId, ct);
}

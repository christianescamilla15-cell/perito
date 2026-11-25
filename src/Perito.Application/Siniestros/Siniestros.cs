using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Privacidad;
using Perito.Application.Puertos;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;

namespace Perito.Application.Siniestros;

/// <summary>Aviso de siniestro (FNOL). Minimización: sólo la póliza, la fecha, la descripción, la estimación y, si hay
/// lesionados, su detalle (dato sensible, cifrado en reposo).</summary>
public sealed record AvisarSiniestro(
    string FolioPoliza, DateOnly FechaOcurrencia, string Descripcion, decimal? Estimacion, string? DetalleDeLesiones)
    : IComando<Resultado<Guid>>;

/// <summary>Triaje por una persona; puede citar la propuesta del asistente que tomó como base.</summary>
public sealed record TriarSiniestro(Guid SiniestroId, TipoDeSiniestro Tipo, Guid? PropuestaId, string Usuario)
    : IComando<Resultado>;

/// <summary>Asignación de ajustador.</summary>
public sealed record AsignarAjustador(Guid SiniestroId, Guid AjustadorId) : IComando<Resultado>;

/// <summary>Dato o documento recibido.</summary>
public sealed record RegistrarDatoRecibido(Guid SiniestroId, DatoRequerido Dato) : IComando<Resultado>;

/// <summary>Reserva inicial.</summary>
public sealed record ConstituirReserva(Guid SiniestroId, decimal Estimacion) : IComando<Resultado>;

/// <summary>Ajuste de reserva.</summary>
public sealed record AjustarReserva(Guid SiniestroId, decimal NuevoSaldo, string Motivo) : IComando<Resultado>;

/// <summary>Dictamen con los hechos que verificó el ajustador.</summary>
public sealed record Dictaminar(
    Guid SiniestroId, decimal PerdidaAjustada, IReadOnlyCollection<CodigoExclusion> Exclusiones,
    ResultadoDeInvestigacion? Investigacion, string Usuario) : IComando<Resultado>;

/// <summary>Pago de un dictamen procedente.</summary>
public sealed record PagarSiniestro(Guid SiniestroId, string Referencia, string Usuario) : IComando<Resultado>;

/// <summary>Comunicación del rechazo de un dictamen improcedente.</summary>
public sealed record RechazarSiniestro(Guid SiniestroId) : IComando<Resultado>;

/// <summary>Vista de un siniestro para quien lo atiende (incluye la descripción: el acceso queda registrado).</summary>
public sealed record SiniestroVista(
    Guid Id, Guid PolizaId, Guid AseguradoId, Ramo Ramo, EstadoSiniestro Estado, DateOnly FechaOcurrencia,
    DateOnly FechaAviso, string? Descripcion, bool TieneDetalleDeLesiones, decimal? Estimacion,
    TipoDeSiniestro? Tipo, CodigoCobertura? Cobertura, string? MotivoNoElegible, IReadOnlyList<IndicioDeFraude> Indicios,
    bool RequiereInvestigacion, Guid? PropuestaAceptada, Guid? AjustadorId, IReadOnlyList<DatoRequerido> DatosPendientes,
    decimal Reserva, Dictamen? Dictamen, DateOnly? FechaLimiteDePago, Pago? Pago, DateOnly? FechaRechazo);

/// <summary>Consulta un siniestro con su finalidad declarada.</summary>
public sealed record ConsultarSiniestro(Guid SiniestroId, string Usuario, string Rol, string Finalidad)
    : IConsulta<Resultado<SiniestroVista>>;

/// <summary>Manejadores del flujo del siniestro: cargan, aplican la regla del dominio y confirman.</summary>
public sealed class ManejadoresDeSiniestros(
    ISiniestros siniestros, IPolizas polizas, IAjustadores ajustadores, IPropuestasDeTriaje propuestas,
    IRegistroDeAccesos accesos, IUnidadDeTrabajo unidad, IClock reloj)
    : IManejador<AvisarSiniestro, Resultado<Guid>>,
        IManejador<TriarSiniestro, Resultado>,
        IManejador<AsignarAjustador, Resultado>,
        IManejador<RegistrarDatoRecibido, Resultado>,
        IManejador<ConstituirReserva, Resultado>,
        IManejador<AjustarReserva, Resultado>,
        IManejador<Dictaminar, Resultado>,
        IManejador<PagarSiniestro, Resultado>,
        IManejador<RechazarSiniestro, Resultado>,
        IManejador<ConsultarSiniestro, Resultado<SiniestroVista>>
{
    /// <summary>Ventana de la regla de frecuencia (GEN-9): los 12 meses anteriores a la ocurrencia.</summary>
    public const int DiasDeVentanaDeFrecuencia = 365;

    /// <inheritdoc />
    public async Task<Resultado<Guid>> ManejarAsync(AvisarSiniestro s, CancellationToken ct)
    {
        if (await polizas.ObtenerPorFolioAsync(s.FolioPoliza.Trim(), ct) is not { } poliza)
        {
            return Resultado<Guid>.Fallo(Errores.NoEncontrado($"la póliza {s.FolioPoliza}"));
        }

        var ahora = reloj.Ahora;
        var r = Siniestro.Avisar(Guid.NewGuid(), poliza, s.FechaOcurrencia, Zonas.FechaLocal(ahora), s.Descripcion,
            s.Estimacion, s.DetalleDeLesiones, ahora);
        if (!r.EsExito)
        {
            return Resultado<Guid>.Fallo(r.Error!);
        }

        siniestros.Agregar(r.Valor);
        await unidad.ConfirmarAsync(ct);
        return Resultado<Guid>.Exito(r.Valor.Id);
    }

    /// <inheritdoc />
    public Task<Resultado> ManejarAsync(TriarSiniestro s, CancellationToken ct) =>
        ConSiniestro(s.SiniestroId, ct, async (siniestro, poliza) =>
        {
            if (s.PropuestaId is { } id &&
                (await propuestas.ObtenerAsync(id, ct) is not { } propuesta || propuesta.SiniestroId != siniestro.Id))
            {
                return Resultado.Fallo(Errores.NoEncontrado("la propuesta para este siniestro"));
            }

            var previos = await siniestros.ContarDeLaPolizaAsync(poliza.Id, siniestro.Id,
                siniestro.FechaOcurrencia.AddDays(-DiasDeVentanaDeFrecuencia), siniestro.FechaOcurrencia.AddDays(-1),
                ct);
            return siniestro.Triar(s.Tipo, poliza, previos, s.Usuario, s.PropuestaId, reloj.Ahora);
        });

    /// <inheritdoc />
    public Task<Resultado> ManejarAsync(AsignarAjustador s, CancellationToken ct) =>
        ConSiniestro(s.SiniestroId, ct, async (siniestro, _) =>
            await ajustadores.ObtenerAsync(s.AjustadorId, ct) is { } ajustador
                ? siniestro.AsignarAjustador(ajustador, await ajustadores.CasosAbiertosAsync(ajustador.Id, ct),
                    reloj.Ahora)
                : Resultado.Fallo(Errores.NoEncontrado("el ajustador")));

    /// <inheritdoc />
    public Task<Resultado> ManejarAsync(RegistrarDatoRecibido s, CancellationToken ct) =>
        ConSiniestro(s.SiniestroId, ct, (siniestro, _) => Task.FromResult(siniestro.RegistrarDatoRecibido(s.Dato)));

    /// <inheritdoc />
    public Task<Resultado> ManejarAsync(ConstituirReserva s, CancellationToken ct) =>
        ConSiniestro(s.SiniestroId, ct,
            (siniestro, poliza) => Task.FromResult(siniestro.ConstituirReserva(poliza, s.Estimacion, reloj.Ahora)));

    /// <inheritdoc />
    public Task<Resultado> ManejarAsync(AjustarReserva s, CancellationToken ct) =>
        ConSiniestro(s.SiniestroId, ct, (siniestro, poliza) =>
            Task.FromResult(siniestro.AjustarReserva(poliza, s.NuevoSaldo, s.Motivo, reloj.Ahora)));

    /// <inheritdoc />
    public Task<Resultado> ManejarAsync(Dictaminar s, CancellationToken ct) =>
        ConSiniestro(s.SiniestroId, ct, (siniestro, poliza) =>
        {
            var ahora = reloj.Ahora;
            return Task.FromResult(siniestro.Dictaminar(poliza,
                new EntradaDeDictamen(s.PerdidaAjustada, s.Exclusiones, s.Investigacion), s.Usuario,
                Zonas.FechaLocal(ahora), ahora));
        });

    /// <inheritdoc />
    public Task<Resultado> ManejarAsync(PagarSiniestro s, CancellationToken ct) =>
        ConSiniestro(s.SiniestroId, ct, (siniestro, _) =>
        {
            var ahora = reloj.Ahora;
            return Task.FromResult(siniestro.Pagar(s.Referencia, s.Usuario, Zonas.FechaLocal(ahora), ahora));
        });

    /// <inheritdoc />
    public Task<Resultado> ManejarAsync(RechazarSiniestro s, CancellationToken ct) =>
        ConSiniestro(s.SiniestroId, ct, (siniestro, _) =>
        {
            var ahora = reloj.Ahora;
            return Task.FromResult(siniestro.Rechazar(Zonas.FechaLocal(ahora), ahora));
        });

    /// <inheritdoc />
    public async Task<Resultado<SiniestroVista>> ManejarAsync(ConsultarSiniestro s, CancellationToken ct)
    {
        if (!Finalidades.EsValida(s.Finalidad))
        {
            return Resultado<SiniestroVista>.Fallo(Finalidades.ErrorDeFinalidad);
        }

        if (await siniestros.ObtenerAsync(s.SiniestroId, ct) is not { } x)
        {
            return Resultado<SiniestroVista>.Fallo(Errores.NoEncontrado("el siniestro"));
        }

        // Primero el registro, después los datos: si el registro falla, la excepción sube y no se devuelve nada.
        await accesos.RegistrarAsync(new AccesoADatosPersonales(Guid.NewGuid(), reloj.Ahora, s.Usuario, s.Rol,
            "siniestro", x.Id, x.AseguradoId, s.Finalidad), ct);
        return Resultado<SiniestroVista>.Exito(new SiniestroVista(x.Id, x.PolizaId, x.AseguradoId, x.Ramo, x.Estado,
            x.FechaOcurrencia, x.FechaAviso, x.Descripcion, x.DetalleDeLesiones is not null, x.Estimacion, x.Tipo,
            x.Cobertura, x.MotivoNoElegible?.ToString(), x.Indicios.Order().ToList(), x.RequiereInvestigacion,
            x.PropuestaAceptada, x.AjustadorId, x.DatosPendientes, x.Reserva, x.Dictamen, x.FechaLimiteDePago, x.Pago,
            x.FechaRechazo));
    }

    private async Task<Resultado> ConSiniestro(Guid id, CancellationToken ct,
        Func<Siniestro, Poliza, Task<Resultado>> accion)
    {
        if (await siniestros.ObtenerAsync(id, ct) is not { } siniestro)
        {
            return Resultado.Fallo(Errores.NoEncontrado("el siniestro"));
        }

        var poliza = await polizas.ObtenerAsync(siniestro.PolizaId, ct)
            ?? throw new InvalidOperationException($"El siniestro {id} apunta a una póliza que no existe.");
        var resultado = await accion(siniestro, poliza);
        if (resultado.EsExito)
        {
            await unidad.ConfirmarAsync(ct);
        }

        return resultado;
    }
}

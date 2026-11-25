using Perito.Application.Abstracciones;
using Perito.Application.Puertos;
using Perito.Domain.Ajustadores;
using Perito.Domain.Asegurados;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;

namespace Perito.Application.Polizas;

/// <summary>Errores comunes de la aplicación.</summary>
public static class Errores
{
    /// <summary>El recurso no existe.</summary>
    public static Error NoEncontrado(string que) => new("no_encontrado", $"No existe {que}.");
}

/// <summary>Alta de un titular (sólo los datos que el siniestro necesita).</summary>
public sealed record RegistrarAsegurado(string Nombre, string Rfc, string Correo, string Telefono, string? Clabe)
    : IComando<Resultado<Guid>>;

/// <summary>Alta de un ajustador.</summary>
public sealed record RegistrarAjustador(string Nombre, IReadOnlyCollection<Especialidad> Especialidades)
    : IComando<Resultado<Guid>>;

/// <summary>Emisión de una póliza.</summary>
public sealed record EmitirPoliza(
    string Folio, Ramo Ramo, Guid AseguradoId, DateOnly Inicio, DateOnly Fin, DateOnly PrimaPagadaHasta,
    IReadOnlyCollection<CoberturaContratada> Coberturas) : IComando<Resultado<Guid>>;

/// <summary>Una póliza como se publica: sin datos personales del titular, sólo su id.</summary>
public sealed record PolizaVista(
    Guid Id, string Folio, Ramo Ramo, Guid AseguradoId, DateOnly InicioVigencia, DateOnly FinVigencia,
    DateOnly PrimaPagadaHasta, DateOnly? FechaCancelacion, IReadOnlyList<CoberturaContratada> Coberturas);

/// <summary>Consulta una póliza por folio.</summary>
public sealed record ConsultarPoliza(string Folio) : IConsulta<PolizaVista?>;

/// <summary>Lo que el asistente puede saber de la póliza de un siniestro: vigencia y carátula, sin datos personales
/// ni folio.</summary>
public sealed record CoberturasDelSiniestro(
    Ramo Ramo, DateOnly InicioVigencia, DateOnly FinVigencia, bool Cancelada,
    IReadOnlyList<CoberturaContratada> Coberturas);

/// <summary>La consulta que expone la función del asistente, acotada al siniestro que se tría.</summary>
public sealed record ConsultarCoberturasDelSiniestro(Guid SiniestroId) : IConsulta<CoberturasDelSiniestro?>;

/// <summary>Manejadores de titulares, ajustadores y pólizas.</summary>
public sealed class ManejadoresDePolizas(
    IAsegurados asegurados, IAjustadores ajustadores, IPolizas polizas, ISiniestros siniestros,
    IUnidadDeTrabajo unidad)
    : IManejador<RegistrarAsegurado, Resultado<Guid>>,
        IManejador<RegistrarAjustador, Resultado<Guid>>,
        IManejador<EmitirPoliza, Resultado<Guid>>,
        IManejador<ConsultarPoliza, PolizaVista?>,
        IManejador<ConsultarCoberturasDelSiniestro, CoberturasDelSiniestro?>
{
    /// <inheritdoc />
    public async Task<Resultado<Guid>> ManejarAsync(RegistrarAsegurado s, CancellationToken ct)
    {
        var r = Asegurado.Registrar(Guid.NewGuid(), s.Nombre, s.Rfc, s.Correo, s.Telefono, s.Clabe);
        if (!r.EsExito)
        {
            return Resultado<Guid>.Fallo(r.Error!);
        }

        asegurados.Agregar(r.Valor);
        await unidad.ConfirmarAsync(ct);
        return Resultado<Guid>.Exito(r.Valor.Id);
    }

    /// <inheritdoc />
    public async Task<Resultado<Guid>> ManejarAsync(RegistrarAjustador s, CancellationToken ct)
    {
        var r = Ajustador.Registrar(Guid.NewGuid(), s.Nombre, s.Especialidades);
        if (!r.EsExito)
        {
            return Resultado<Guid>.Fallo(r.Error!);
        }

        ajustadores.Agregar(r.Valor);
        await unidad.ConfirmarAsync(ct);
        return Resultado<Guid>.Exito(r.Valor.Id);
    }

    /// <inheritdoc />
    public async Task<Resultado<Guid>> ManejarAsync(EmitirPoliza s, CancellationToken ct)
    {
        if (await asegurados.ObtenerAsync(s.AseguradoId, ct) is null)
        {
            return Resultado<Guid>.Fallo(Errores.NoEncontrado("el titular"));
        }

        if (await polizas.ObtenerPorFolioAsync(s.Folio.Trim(), ct) is not null)
        {
            return Resultado<Guid>.Fallo("folio_duplicado", $"Ya existe la póliza {s.Folio}.");
        }

        var r = Poliza.Emitir(Guid.NewGuid(), s.Folio, s.Ramo, s.AseguradoId, s.Inicio, s.Fin, s.PrimaPagadaHasta,
            s.Coberturas);
        if (!r.EsExito)
        {
            return Resultado<Guid>.Fallo(r.Error!);
        }

        polizas.Agregar(r.Valor);
        await unidad.ConfirmarAsync(ct);
        return Resultado<Guid>.Exito(r.Valor.Id);
    }

    /// <inheritdoc />
    public async Task<PolizaVista?> ManejarAsync(ConsultarPoliza s, CancellationToken ct) =>
        await polizas.ObtenerPorFolioAsync(s.Folio, ct) is { } p
            ? new PolizaVista(p.Id, p.Folio, p.Ramo, p.AseguradoId, p.InicioVigencia, p.FinVigencia,
                p.PrimaPagadaHasta, p.FechaCancelacion, p.Coberturas)
            : null;

    /// <inheritdoc />
    public async Task<CoberturasDelSiniestro?> ManejarAsync(ConsultarCoberturasDelSiniestro s, CancellationToken ct)
    {
        if (await siniestros.ObtenerAsync(s.SiniestroId, ct) is not { } siniestro ||
            await polizas.ObtenerAsync(siniestro.PolizaId, ct) is not { } p)
        {
            return null;
        }

        return new CoberturasDelSiniestro(p.Ramo, p.InicioVigencia, p.FinVigencia, p.FechaCancelacion is not null,
            p.Coberturas);
    }
}

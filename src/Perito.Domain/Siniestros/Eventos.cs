using Perito.Domain.Comun;
using Perito.Domain.Producto;

namespace Perito.Domain.Siniestros;

// Los eventos llevan identificadores, ramo, montos y estados: nunca nombres, contactos ni descripciones (prueba en
// Perito.Infrastructure.Tests sobre la bandeja de salida).

/// <summary>Se recibió el aviso.</summary>
public sealed record SiniestroAvisado(Guid Id, DateTimeOffset OcurridoEn, Guid SiniestroId, Guid PolizaId, Ramo Ramo)
    : IEventoDeDominio;

/// <summary>Se trió.</summary>
public sealed record SiniestroTriado(
    Guid Id, DateTimeOffset OcurridoEn, Guid SiniestroId, Ramo Ramo, TipoDeSiniestro Tipo, CodigoCobertura Cobertura,
    bool Elegible, bool RequiereInvestigacion) : IEventoDeDominio;

/// <summary>Se asignó un ajustador.</summary>
public sealed record AjustadorAsignado(Guid Id, DateTimeOffset OcurridoEn, Guid SiniestroId, Guid AjustadorId)
    : IEventoDeDominio;

/// <summary>Se constituyó la reserva.</summary>
public sealed record ReservaConstituida(
    Guid Id, DateTimeOffset OcurridoEn, Guid SiniestroId, Ramo Ramo, CodigoCobertura Cobertura, decimal Monto)
    : IEventoDeDominio;

/// <summary>Se ajustó la reserva (delta con signo y saldo resultante).</summary>
public sealed record ReservaAjustada(
    Guid Id, DateTimeOffset OcurridoEn, Guid SiniestroId, Ramo Ramo, decimal Delta, decimal Saldo) : IEventoDeDominio;

/// <summary>Se dictaminó.</summary>
public sealed record SiniestroDictaminado(
    Guid Id, DateTimeOffset OcurridoEn, Guid SiniestroId, Ramo Ramo, bool Procedente, MotivoDeDictamen Motivo,
    decimal Indemnizacion) : IEventoDeDominio;

/// <summary>Se pagó; la reserva liberada sale del saldo del ramo.</summary>
public sealed record PagoEmitido(
    Guid Id, DateTimeOffset OcurridoEn, Guid SiniestroId, Ramo Ramo, decimal Monto, decimal ReservaLiberada)
    : IEventoDeDominio;

/// <summary>Se comunicó el rechazo; la reserva liberada sale del saldo del ramo.</summary>
public sealed record SiniestroRechazado(
    Guid Id, DateTimeOffset OcurridoEn, Guid SiniestroId, Ramo Ramo, MotivoDeDictamen Motivo, string? Clausula,
    decimal ReservaLiberada) : IEventoDeDominio;

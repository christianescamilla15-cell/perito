using Perito.Application.Abstracciones;
using Perito.Application.Puertos;

namespace Perito.Application.Lecturas;

/// <summary>Saldos de reserva por ramo, de la proyección que mantiene el consumidor de eventos.</summary>
public sealed record ConsultarTableroDeReservas : IConsulta<IReadOnlyList<SaldoDeReservaPorRamo>>;

/// <summary>Manejador del tablero.</summary>
public sealed class ManejadorDelTablero(ITableroDeReservas tablero)
    : IManejador<ConsultarTableroDeReservas, IReadOnlyList<SaldoDeReservaPorRamo>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<SaldoDeReservaPorRamo>> ManejarAsync(ConsultarTableroDeReservas s, CancellationToken ct) =>
        tablero.LeerAsync(ct);
}

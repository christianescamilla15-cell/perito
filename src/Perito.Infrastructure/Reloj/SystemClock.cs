using Perito.Domain.Comun;

namespace Perito.Infrastructure.Reloj;

/// <summary>El ÚNICO lugar que lee el reloj del sistema (lo comprueba una prueba de arquitectura sobre el IL).</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
}

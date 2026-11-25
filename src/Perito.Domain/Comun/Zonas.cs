namespace Perito.Domain.Comun;

/// <summary>Las fechas del negocio (aviso, dictamen, pago) son días del calendario de la Ciudad de México; los
/// instantes se guardan en UTC.</summary>
public static class Zonas
{
    /// <summary>Zona del negocio.</summary>
    public static readonly TimeZoneInfo CiudadDeMexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    /// <summary>El día del calendario local en que cae un instante.</summary>
    public static DateOnly FechaLocal(DateTimeOffset instante) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instante, CiudadDeMexico).DateTime);
}

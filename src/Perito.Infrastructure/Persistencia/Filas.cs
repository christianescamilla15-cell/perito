namespace Perito.Infrastructure.Persistencia;

// Filas de la base: la forma relacional del dominio. Nunca salen de la infraestructura (prueba de arquitectura).
// Los campos *Cifrado/*Cifrada son bytea con AES-256-GCM (Privacidad/Cifrador.cs).

/// <summary>Titular.</summary>
public sealed class AseguradoFila
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public byte[]? RfcCifrado { get; set; }

    public byte[]? CorreoCifrado { get; set; }

    public byte[]? TelefonoCifrado { get; set; }

    public byte[]? ClabeCifrada { get; set; }

    public DateTimeOffset? AnonimizadoEn { get; set; }

    public uint Version { get; set; }
}

/// <summary>Póliza.</summary>
public sealed class PolizaFila
{
    public Guid Id { get; set; }

    public string Folio { get; set; } = string.Empty;

    public string Ramo { get; set; } = string.Empty;

    public Guid AseguradoId { get; set; }

    public DateOnly InicioVigencia { get; set; }

    public DateOnly FinVigencia { get; set; }

    public DateOnly PrimaPagadaHasta { get; set; }

    public DateOnly? FechaCancelacion { get; set; }

    public List<CoberturaFila> Coberturas { get; set; } = [];

    public uint Version { get; set; }
}

/// <summary>Cobertura de la carátula (llave: póliza + código).</summary>
public sealed class CoberturaFila
{
    public Guid PolizaId { get; set; }

    public string Codigo { get; set; } = string.Empty;

    public decimal SumaAsegurada { get; set; }

    public string TipoDeDeducible { get; set; } = string.Empty;

    public decimal ValorDeDeducible { get; set; }

    public decimal Coaseguro { get; set; }
}

/// <summary>Ajustador.</summary>
public sealed class AjustadorFila
{
    public Guid Id { get; set; }

    public string Nombre { get; set; } = string.Empty;

    public List<string> Especialidades { get; set; } = [];

    public bool Activo { get; set; }

    public uint Version { get; set; }
}

/// <summary>Siniestro: estado, triaje, dictamen y pago en columnas; los movimientos de reserva en su tabla.</summary>
public sealed class SiniestroFila
{
    public Guid Id { get; set; }

    public Guid PolizaId { get; set; }

    public Guid AseguradoId { get; set; }

    public string Ramo { get; set; } = string.Empty;

    public DateOnly FechaOcurrencia { get; set; }

    public DateOnly FechaAviso { get; set; }

    public byte[]? DescripcionCifrada { get; set; }

    public byte[]? LesionesCifradas { get; set; }

    public decimal? Estimacion { get; set; }

    public string Estado { get; set; } = string.Empty;

    public string? Tipo { get; set; }

    public string? Cobertura { get; set; }

    public string? MotivoNoElegible { get; set; }

    public List<string> Indicios { get; set; } = [];

    public string? TriadoPor { get; set; }

    public Guid? PropuestaAceptada { get; set; }

    public Guid? AjustadorId { get; set; }

    public List<string> DatosRecibidos { get; set; } = [];

    public bool? DictamenProcedente { get; set; }

    public string? DictamenMotivo { get; set; }

    public string? DictamenClausula { get; set; }

    public decimal? PerdidaAjustada { get; set; }

    public decimal? Indemnizacion { get; set; }

    public DateOnly? FechaDictamen { get; set; }

    public string? DictaminadoPor { get; set; }

    public List<string> ExclusionesConfirmadas { get; set; } = [];

    public DateOnly? FechaLimiteDePago { get; set; }

    public string? PagoReferencia { get; set; }

    public decimal? PagoMonto { get; set; }

    public DateOnly? FechaPago { get; set; }

    public string? PagadoPor { get; set; }

    public bool? PagoFueraDePlazo { get; set; }

    public DateOnly? FechaRechazo { get; set; }

    public List<MovimientoDeReservaFila> Movimientos { get; set; } = [];

    public uint Version { get; set; }
}

/// <summary>Movimiento de reserva (llave: siniestro + secuencia).</summary>
public sealed class MovimientoDeReservaFila
{
    public Guid SiniestroId { get; set; }

    public int Secuencia { get; set; }

    public DateTimeOffset Fecha { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public decimal Monto { get; set; }

    public decimal Saldo { get; set; }

    public string Motivo { get; set; } = string.Empty;
}

/// <summary>Propuesta del asistente como documento JSON (jsonb): se consulta entera, no se reporta por campos.</summary>
public sealed class PropuestaFila
{
    public Guid Id { get; set; }

    public Guid SiniestroId { get; set; }

    public DateTimeOffset GeneradaEn { get; set; }

    public string Documento { get; set; } = "{}";
}

/// <summary>Bandeja de salida (outbox): se escribe en la MISMA transacción que el cambio de estado.</summary>
public sealed class MensajeDeSalida
{
    public Guid Id { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public Guid Agregado { get; set; }

    public string Contenido { get; set; } = "{}";

    public DateTimeOffset OcurridoEn { get; set; }

    public long Secuencia { get; set; }

    public DateTimeOffset? ProcesadoEn { get; set; }

    public int Intentos { get; set; }

    public DateTimeOffset? SiguienteIntento { get; set; }

    public string? UltimoError { get; set; }

    public bool Muerto { get; set; }
}

/// <summary>Bandeja de entrada de un consumidor: un mensaje se aplica una sola vez por consumidor.</summary>
public sealed class MensajeRecibido
{
    public string Consumidor { get; set; } = string.Empty;

    public Guid MensajeId { get; set; }

    public DateTimeOffset ProcesadoEn { get; set; }
}

/// <summary>Proyección: saldo de reservas por ramo.</summary>
public sealed class SaldoDeReservaFila
{
    public string Ramo { get; set; } = string.Empty;

    public decimal ReservaVigente { get; set; }

    public int SiniestrosConReserva { get; set; }
}

/// <summary>Registro de accesos a datos personales (sin los datos).</summary>
public sealed class AccesoFila
{
    public Guid Id { get; set; }

    public DateTimeOffset Fecha { get; set; }

    public string Usuario { get; set; } = string.Empty;

    public string Rol { get; set; } = string.Empty;

    public string Recurso { get; set; } = string.Empty;

    public Guid RecursoId { get; set; }

    public Guid TitularId { get; set; }

    public string Finalidad { get; set; } = string.Empty;
}

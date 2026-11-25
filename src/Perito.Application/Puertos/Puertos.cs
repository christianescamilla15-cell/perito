using Perito.Domain.Ajustadores;
using Perito.Domain.Asegurados;
using Perito.Domain.Polizas;
using Perito.Domain.Privacidad;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;

namespace Perito.Application.Puertos;

/// <summary>Titulares. Lo que se obtiene o agrega queda a cargo de la unidad de trabajo.</summary>
public interface IAsegurados
{
    /// <summary>Obtiene un titular.</summary>
    Task<Asegurado?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Agrega un titular nuevo.</summary>
    void Agregar(Asegurado asegurado);
}

/// <summary>Pólizas.</summary>
public interface IPolizas
{
    /// <summary>Obtiene una póliza por su id.</summary>
    Task<Poliza?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Obtiene una póliza por su folio.</summary>
    Task<Poliza?> ObtenerPorFolioAsync(string folio, CancellationToken ct);

    /// <summary>Agrega una póliza nueva.</summary>
    void Agregar(Poliza poliza);
}

/// <summary>Ajustadores.</summary>
public interface IAjustadores
{
    /// <summary>Obtiene un ajustador.</summary>
    Task<Ajustador?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Casos abiertos (asignados y no pagados ni rechazados) del ajustador.</summary>
    Task<int> CasosAbiertosAsync(Guid ajustadorId, CancellationToken ct);

    /// <summary>Agrega un ajustador.</summary>
    void Agregar(Ajustador ajustador);
}

/// <summary>Siniestros.</summary>
public interface ISiniestros
{
    /// <summary>Obtiene un siniestro.</summary>
    Task<Siniestro?> ObtenerAsync(Guid id, CancellationToken ct);

    /// <summary>Agrega un siniestro.</summary>
    void Agregar(Siniestro siniestro);

    /// <summary>Siniestros de la póliza ocurridos en [desde, hasta], sin contar uno (GEN-9: frecuencia).</summary>
    Task<int> ContarDeLaPolizaAsync(Guid polizaId, Guid excepto, DateOnly desde, DateOnly hasta, CancellationToken ct);

    /// <summary>Todos los siniestros de un titular (para anonimizar).</summary>
    Task<IReadOnlyList<Siniestro>> DelAseguradoAsync(Guid aseguradoId, CancellationToken ct);
}

/// <summary>Propuestas del asistente: documentos de sólo consulta que nunca cambian un siniestro.</summary>
public interface IPropuestasDeTriaje
{
    /// <summary>Guarda una propuesta.</summary>
    void Agregar(PropuestaDeTriaje propuesta);

    /// <summary>Obtiene una propuesta.</summary>
    Task<PropuestaDeTriaje?> ObtenerAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Guarda de una vez lo que se obtuvo o agregó en la solicitud y los eventos de dominio en la bandeja de salida, en la
/// misma transacción. Un cambio concurrente sobre el mismo agregado lanza <see cref="ConflictoDeConcurrencia"/>.
/// </summary>
public interface IUnidadDeTrabajo
{
    /// <summary>Confirma la transacción.</summary>
    Task ConfirmarAsync(CancellationToken ct);
}

/// <summary>Otro cambio guardó el mismo agregado primero.</summary>
public sealed class ConflictoDeConcurrencia(string mensaje, Exception? causa = null) : Exception(mensaje, causa);

/// <summary>Un acceso a datos personales: quién, cuándo, a qué, de quién y para qué. Sin los datos mismos.</summary>
public sealed record AccesoADatosPersonales(
    Guid Id, DateTimeOffset Fecha, string Usuario, string Rol, string Recurso, Guid RecursoId, Guid TitularId,
    string Finalidad);

/// <summary>Registro de accesos a datos personales (GEN-12). Se escribe ANTES de devolver los datos: si no se puede
/// registrar, no se devuelven.</summary>
public interface IRegistroDeAccesos
{
    /// <summary>Registra un acceso de inmediato (fuera de la unidad de trabajo).</summary>
    Task RegistrarAsync(AccesoADatosPersonales acceso, CancellationToken ct);

    /// <summary>Los accesos a los datos de un titular (derecho de acceso).</summary>
    Task<IReadOnlyList<AccesoADatosPersonales>> DelTitularAsync(Guid titularId, CancellationToken ct);
}

/// <summary>Señales de riesgo que el asistente puede leer en el texto. No deciden nada (el riesgo que cuenta es el de
/// GEN-9 con datos estructurados).</summary>
public enum SenalDeRiesgo
{
    /// <summary>Nadie vio nada, sin cámaras ni evidencia.</summary>
    SinTestigosNiEvidencia,

    /// <summary>Bienes de valor sin factura o pagados en efectivo.</summary>
    BienesSinComprobante,

    /// <summary>El relato cambia o se contradice.</summary>
    RelatoContradictorio,

    /// <summary>Dice que acaba de contratar.</summary>
    ContratacionReciente,

    /// <summary>Presiona por un pago inmediato.</summary>
    PresionPorPagoRapido,
}

/// <summary>Lo que se le entrega al asistente: un identificador opaco, el ramo y el texto YA seudonimizado.</summary>
public sealed record SolicitudAlAsistente(Guid SiniestroId, Ramo Ramo, TextoSeudonimizado Descripcion);

/// <summary>Lo que el asistente devuelve, tal cual lo interpretó de la respuesta del modelo (sin validar aún).</summary>
public sealed record PropuestaGenerada(
    string Proveedor, string Modelo, string? Tipo, string? Clausula, IReadOnlyList<string> DatosFaltantes,
    IReadOnlyList<string> Exclusiones, IReadOnlyList<string> Senales, string Justificacion,
    IReadOnlyList<string> ClausulasRecuperadas, IReadOnlyList<string> Observaciones, long LatenciaMs);

/// <summary>Asistente de triaje: propone; nunca decide ni recibe datos personales en claro.</summary>
public interface IAsistenteDeTriaje
{
    /// <summary>Genera una propuesta para un siniestro.</summary>
    Task<PropuestaGenerada> ProponerAsync(SolicitudAlAsistente solicitud, CancellationToken ct);
}

/// <summary>La propuesta validada que ve el ajustador. La cobertura y si está contratada las calcula la aplicación,
/// no el modelo; la cita sólo se conserva si está entre las cláusulas recuperadas y corresponde al tipo.</summary>
public sealed record PropuestaDeTriaje(
    Guid Id, Guid SiniestroId, DateTimeOffset GeneradaEn, string GeneradaPor, string Proveedor, string Modelo,
    TipoDeSiniestro? Tipo, CodigoCobertura? Cobertura, string? Clausula, bool CitaSustentada,
    bool? CoberturaContratada, IReadOnlyList<DatoRequerido> DatosFaltantes, IReadOnlyList<string> Exclusiones,
    IReadOnlyList<SenalDeRiesgo> Senales, string Justificacion, IReadOnlyList<string> ClausulasRecuperadas,
    IReadOnlyList<string> Observaciones, long LatenciaMs);

/// <summary>Saldo de reservas por ramo (proyección que mantiene el consumidor de eventos).</summary>
public sealed record SaldoDeReservaPorRamo(Ramo Ramo, decimal ReservaVigente, int SiniestrosConReserva);

/// <summary>Lectura del tablero de reservas.</summary>
public interface ITableroDeReservas
{
    /// <summary>Saldos por ramo.</summary>
    Task<IReadOnlyList<SaldoDeReservaPorRamo>> LeerAsync(CancellationToken ct);
}

/// <summary>
/// Un consumidor de eventos de la bandeja de salida. La entrega es «al menos una vez»: el consumidor DEBE ser
/// idempotente por el id del mensaje (la infraestructura lo hace con una bandeja de entrada).
/// </summary>
public interface IConsumidorDeEventos
{
    /// <summary>Nombre estable (llave de su bandeja de entrada).</summary>
    string Nombre { get; }

    /// <summary>Si le interesa el evento.</summary>
    bool Atiende(Domain.Comun.IEventoDeDominio evento);

    /// <summary>Aplica el evento; recibir dos veces el mismo mensaje no cambia el resultado.</summary>
    Task ConsumirAsync(Guid mensajeId, Domain.Comun.IEventoDeDominio evento, CancellationToken ct);
}

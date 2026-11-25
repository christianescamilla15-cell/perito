namespace Perito.Domain.Comun;

/// <summary>Un error de negocio con código estable (lo que ve la API) y mensaje para personas.</summary>
public sealed record Error(string Codigo, string Mensaje)
{
    /// <summary>Transición no permitida en la máquina de estados.</summary>
    public static Error TransicionInvalida(string accion, object estado) =>
        new("transicion_invalida", $"No se puede {accion} un siniestro en estado {estado}.");
}

/// <summary>Resultado de una operación de dominio: éxito o un error con nombre, nunca una excepción de negocio.</summary>
public class Resultado
{
    protected Resultado(Error? error) => Error = error;

    /// <summary>El error, si lo hubo.</summary>
    public Error? Error { get; }

    /// <summary>Verdadero si la operación se aplicó.</summary>
    public bool EsExito => Error is null;

    /// <summary>Éxito sin valor.</summary>
    public static Resultado Exito() => new(null);

    /// <summary>Fallo con su error.</summary>
    public static Resultado Fallo(Error error) => new(error);

    /// <summary>Fallo con código y mensaje.</summary>
    public static Resultado Fallo(string codigo, string mensaje) => new(new Error(codigo, mensaje));
}

/// <summary>Resultado con valor.</summary>
public sealed class Resultado<T> : Resultado
{
    private readonly T? _valor;

    private Resultado(T? valor, Error? error) : base(error) => _valor = valor;

    /// <summary>El valor; leerlo en un fallo es un error de programación.</summary>
    public T Valor => EsExito ? _valor! : throw new InvalidOperationException($"Resultado fallido: {Error!.Codigo}");

    /// <summary>Éxito con valor.</summary>
    public static Resultado<T> Exito(T valor) => new(valor, null);

    /// <summary>Fallo con su error.</summary>
    public static new Resultado<T> Fallo(Error error) => new(default, error);

    /// <summary>Fallo con código y mensaje.</summary>
    public static new Resultado<T> Fallo(string codigo, string mensaje) => new(default, new Error(codigo, mensaje));
}

/// <summary>El reloj se inyecta: ninguna regla lee la hora del sistema (lo hace cumplir una prueba de arquitectura).</summary>
public interface IClock
{
    /// <summary>El instante actual.</summary>
    DateTimeOffset Ahora { get; }
}

/// <summary>Un hecho del dominio que otros quieren saber. Sólo lleva identificadores, montos y estados: nunca datos
/// personales (lo comprueba una prueba sobre la bandeja de salida).</summary>
public interface IEventoDeDominio
{
    /// <summary>Identificador único: la llave de idempotencia de los consumidores.</summary>
    Guid Id { get; }

    /// <summary>Cuándo ocurrió.</summary>
    DateTimeOffset OcurridoEn { get; }
}

/// <summary>Raíz de agregado: junta los eventos que sus operaciones producen hasta que la unidad de trabajo los
/// guarda en la bandeja de salida, en la misma transacción que el cambio de estado.</summary>
public abstract class RaizDeAgregado
{
    private readonly List<IEventoDeDominio> _eventos = [];

    /// <summary>Identificador del agregado.</summary>
    public Guid Id { get; protected init; }

    /// <summary>Eventos pendientes de guardar.</summary>
    public IReadOnlyList<IEventoDeDominio> Eventos => _eventos;

    /// <summary>Agrega un evento pendiente.</summary>
    protected void Registrar(IEventoDeDominio evento) => _eventos.Add(evento);

    /// <summary>Entrega los eventos pendientes y los olvida (los toma la unidad de trabajo).</summary>
    public IReadOnlyList<IEventoDeDominio> TomarEventos()
    {
        var copia = _eventos.ToList();
        _eventos.Clear();
        return copia;
    }
}

/// <summary>Cantidades en pesos: se redondean al centavo, la mitad hacia arriba (GEN-7).</summary>
public static class Pesos
{
    /// <summary>Redondea al centavo, la mitad lejos del cero.</summary>
    public static decimal Redondear(decimal monto) => Math.Round(monto, 2, MidpointRounding.AwayFromZero);
}

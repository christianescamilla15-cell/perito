using Perito.Application.Puertos;
using Perito.Domain.Ajustadores;
using Perito.Domain.Asegurados;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Siniestros;

namespace Perito.EnMemoria;

/// <summary>
/// Estado guardado en memoria: lo que «está en la base». Las sesiones leen COPIAS y sólo al confirmar escriben de
/// vuelta, así que un manejador que olvida confirmar pierde sus cambios igual que con PostgreSQL.
/// </summary>
public sealed class AlmacenEnMemoria
{
    internal readonly Dictionary<Guid, Asegurado> Asegurados = [];
    internal readonly Dictionary<Guid, Poliza> Polizas = [];
    internal readonly Dictionary<Guid, Ajustador> Ajustadores = [];
    internal readonly Dictionary<Guid, EstadoGuardado> Siniestros = [];
    internal readonly Dictionary<Guid, PropuestaDeTriaje> Propuestas = [];
    private readonly List<IEventoDeDominio> _bandeja = [];
    private readonly List<AccesoADatosPersonales> _accesos = [];
    private readonly Lock _candado = new();

    /// <summary>Eventos confirmados, en orden (la «bandeja de salida»).</summary>
    public IReadOnlyList<IEventoDeDominio> BandejaDeSalida
    {
        get
        {
            lock (_candado)
            {
                return _bandeja.ToList();
            }
        }
    }

    /// <summary>Accesos a datos personales registrados.</summary>
    public IReadOnlyList<AccesoADatosPersonales> Accesos
    {
        get
        {
            lock (_candado)
            {
                return _accesos.ToList();
            }
        }
    }

    /// <summary>Veces que se confirmó una unidad de trabajo.</summary>
    public int Confirmaciones { get; private set; }

    /// <summary>Estado guardado de un siniestro (para comparar antes y después).</summary>
    public EstadoGuardado? SiniestroGuardado(Guid id)
    {
        lock (_candado)
        {
            return Siniestros.GetValueOrDefault(id);
        }
    }

    internal void Confirmar(IEnumerable<RaizDeAgregado> agregados, IEnumerable<PropuestaDeTriaje> propuestas)
    {
        lock (_candado)
        {
            foreach (var agregado in agregados)
            {
                switch (agregado)
                {
                    case Asegurado a:
                        Asegurados[a.Id] = Copias.De(a);
                        break;
                    case Poliza p:
                        Polizas[p.Id] = Copias.De(p);
                        break;
                    case Ajustador j:
                        Ajustadores[j.Id] = Copias.De(j);
                        break;
                    case Siniestro s:
                        Siniestros[s.Id] = s.Instantanea();
                        break;
                    default:
                        throw new InvalidOperationException($"Agregado desconocido: {agregado.GetType().Name}");
                }

                _bandeja.AddRange(agregado.TomarEventos());
            }

            foreach (var p in propuestas)
            {
                Propuestas[p.Id] = p;
            }

            Confirmaciones++;
        }
    }

    internal void RegistrarAcceso(AccesoADatosPersonales acceso)
    {
        lock (_candado)
        {
            _accesos.Add(acceso);
        }
    }

    internal T Leer<T>(Func<T> lectura)
    {
        lock (_candado)
        {
            return lectura();
        }
    }
}

/// <summary>Una sesión (el alcance de una solicitud): repositorios y unidad de trabajo sobre el almacén.</summary>
public sealed class SesionEnMemoria(AlmacenEnMemoria almacen)
    : IAsegurados, IPolizas, IAjustadores, ISiniestros, IPropuestasDeTriaje, IUnidadDeTrabajo
{
    private readonly Dictionary<Guid, RaizDeAgregado> _rastreados = [];
    private readonly List<PropuestaDeTriaje> _propuestasNuevas = [];

    /// <inheritdoc />
    public Task<Asegurado?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(
        Rastrear(almacen.Leer(() => almacen.Asegurados.TryGetValue(id, out var a) ? Copias.De(a) : null)));

    Task<Poliza?> IPolizas.ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(
        Rastrear(almacen.Leer(() => almacen.Polizas.TryGetValue(id, out var p) ? Copias.De(p) : null)));

    /// <inheritdoc />
    public Task<Poliza?> ObtenerPorFolioAsync(string folio, CancellationToken ct) => Task.FromResult(
        Rastrear(almacen.Leer(() => almacen.Polizas.Values.FirstOrDefault(p => p.Folio == folio) is { } p
            ? Copias.De(p)
            : null)));

    Task<Ajustador?> IAjustadores.ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(
        Rastrear(almacen.Leer(() => almacen.Ajustadores.TryGetValue(id, out var a) ? Copias.De(a) : null)));

    /// <inheritdoc />
    public Task<int> CasosAbiertosAsync(Guid ajustadorId, CancellationToken ct) => Task.FromResult(almacen.Leer(() =>
        almacen.Siniestros.Values.Count(s => s.AjustadorId == ajustadorId &&
            s.Estado is not (EstadoSiniestro.Pagado or EstadoSiniestro.Rechazado))));

    Task<Siniestro?> ISiniestros.ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult(
        Rastrear(almacen.Leer(() => almacen.Siniestros.TryGetValue(id, out var s) ? Siniestro.Rehidratar(s) : null)));

    /// <inheritdoc />
    public Task<int> ContarDeLaPolizaAsync(Guid polizaId, Guid excepto, DateOnly desde, DateOnly hasta,
        CancellationToken ct) => Task.FromResult(almacen.Leer(() => almacen.Siniestros.Values.Count(s =>
        s.PolizaId == polizaId && s.Id != excepto && s.FechaOcurrencia >= desde && s.FechaOcurrencia <= hasta)));

    /// <inheritdoc />
    public Task<IReadOnlyList<Siniestro>> DelAseguradoAsync(Guid aseguradoId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Siniestro>>(almacen.Leer(() => almacen.Siniestros.Values
                .Where(s => s.AseguradoId == aseguradoId).Select(Siniestro.Rehidratar).ToList())
            .Select(s => Rastrear(s)!).ToList());

    Task<PropuestaDeTriaje?> IPropuestasDeTriaje.ObtenerAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(almacen.Leer(() => almacen.Propuestas.GetValueOrDefault(id)));

    /// <inheritdoc />
    public void Agregar(Asegurado asegurado) => Rastrear(asegurado);

    /// <inheritdoc />
    public void Agregar(Poliza poliza) => Rastrear(poliza);

    /// <inheritdoc />
    public void Agregar(Ajustador ajustador) => Rastrear(ajustador);

    /// <inheritdoc />
    public void Agregar(Siniestro siniestro) => Rastrear(siniestro);

    /// <inheritdoc />
    public void Agregar(PropuestaDeTriaje propuesta) => _propuestasNuevas.Add(propuesta);

    /// <inheritdoc />
    public Task ConfirmarAsync(CancellationToken ct)
    {
        almacen.Confirmar(_rastreados.Values, _propuestasNuevas);
        _propuestasNuevas.Clear();
        return Task.CompletedTask;
    }

    private T? Rastrear<T>(T? agregado) where T : RaizDeAgregado
    {
        if (agregado is null)
        {
            return null;
        }

        // La misma instancia dentro de una sesión, como el rastreo de cambios de EF.
        if (_rastreados.TryGetValue(agregado.Id, out var existente))
        {
            return (T)existente;
        }

        _rastreados[agregado.Id] = agregado;
        return agregado;
    }
}

/// <summary>Registro de accesos en memoria.</summary>
public sealed class RegistroDeAccesosEnMemoria(AlmacenEnMemoria almacen) : IRegistroDeAccesos
{
    /// <inheritdoc />
    public Task RegistrarAsync(AccesoADatosPersonales acceso, CancellationToken ct)
    {
        almacen.RegistrarAcceso(acceso);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccesoADatosPersonales>> DelTitularAsync(Guid titularId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AccesoADatosPersonales>>(
            almacen.Accesos.Where(a => a.TitularId == titularId).OrderBy(a => a.Fecha).ToList());
}

/// <summary>Reloj fijo que se puede adelantar.</summary>
public sealed class RelojFijo(DateTimeOffset inicio) : IClock
{
    /// <inheritdoc />
    public DateTimeOffset Ahora { get; private set; } = inicio;

    /// <summary>Adelanta el reloj.</summary>
    public void Adelantar(TimeSpan lapso) => Ahora += lapso;
}

internal static class Copias
{
    public static Asegurado De(Asegurado a) =>
        Asegurado.Rehidratar(a.Id, a.Nombre, a.Rfc, a.Correo, a.Telefono, a.Clabe, a.AnonimizadoEn);

    public static Poliza De(Poliza p) => Poliza.Rehidratar(p.Id, p.Folio, p.Ramo, p.AseguradoId, p.InicioVigencia,
        p.FinVigencia, p.PrimaPagadaHasta, p.FechaCancelacion, p.Coberturas);

    public static Ajustador De(Ajustador a) => Ajustador.Rehidratar(a.Id, a.Nombre, a.Especialidades, a.Activo);
}

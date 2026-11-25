using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Perito.Application.Puertos;
using Perito.Domain.Ajustadores;
using Perito.Domain.Asegurados;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using Perito.Infrastructure.Eventos;
using Perito.Infrastructure.Privacidad;

namespace Perito.Infrastructure.Persistencia;

/// <summary>
/// Repositorios y unidad de trabajo sobre EF Core. Cada agregado que se obtiene o agrega queda rastreado junto con su
/// fila; al confirmar, el estado del agregado se copia a la fila (cifrando sólo lo que cambió, para que una lectura no
/// reescriba la fila) y sus eventos entran a la bandeja de salida en la MISMA transacción.
/// </summary>
public sealed class AlmacenEf(PeritoDbContext db, CifradorDeCampos cifrador)
    : IAsegurados, IPolizas, IAjustadores, ISiniestros, IPropuestasDeTriaje, IUnidadDeTrabajo
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dictionary<Guid, Rastreo> _rastreados = [];

    private sealed record Rastreo(RaizDeAgregado Agregado, object Fila, Dictionary<string, string?> Claros);

    // ── Titulares ───────────────────────────────────────────────────────────────────────────────────────────────
    /// <inheritdoc />
    public async Task<Asegurado?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        if (_rastreados.TryGetValue(id, out var r))
        {
            return (Asegurado)r.Agregado;
        }

        var fila = await db.Asegurados.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (fila is null)
        {
            return null;
        }

        var claros = new Dictionary<string, string?>
        {
            ["rfc"] = Descifrar(fila.RfcCifrado, "asegurados.rfc"),
            ["correo"] = Descifrar(fila.CorreoCifrado, "asegurados.correo"),
            ["telefono"] = Descifrar(fila.TelefonoCifrado, "asegurados.telefono"),
            ["clabe"] = Descifrar(fila.ClabeCifrada, "asegurados.clabe"),
        };
        var a = Asegurado.Rehidratar(fila.Id, fila.Nombre, claros["rfc"], claros["correo"], claros["telefono"],
            claros["clabe"], fila.AnonimizadoEn);
        _rastreados[id] = new Rastreo(a, fila, claros);
        return a;
    }

    /// <inheritdoc />
    public void Agregar(Asegurado asegurado)
    {
        var fila = new AseguradoFila { Id = asegurado.Id };
        db.Asegurados.Add(fila);
        _rastreados[asegurado.Id] = new Rastreo(asegurado, fila, []);
    }

    // ── Pólizas ─────────────────────────────────────────────────────────────────────────────────────────────────
    async Task<Poliza?> IPolizas.ObtenerAsync(Guid id, CancellationToken ct) =>
        _rastreados.TryGetValue(id, out var r)
            ? (Poliza)r.Agregado
            : Rastrear(await db.Polizas.Include(p => p.Coberturas).FirstOrDefaultAsync(p => p.Id == id, ct));

    /// <inheritdoc />
    public async Task<Poliza?> ObtenerPorFolioAsync(string folio, CancellationToken ct)
    {
        var fila = await db.Polizas.Include(p => p.Coberturas).FirstOrDefaultAsync(p => p.Folio == folio, ct);
        return fila is not null && _rastreados.TryGetValue(fila.Id, out var r) ? (Poliza)r.Agregado : Rastrear(fila);
    }

    /// <inheritdoc />
    public void Agregar(Poliza poliza)
    {
        var fila = new PolizaFila
        {
            Id = poliza.Id, Folio = poliza.Folio, Ramo = poliza.Ramo.ToString(), AseguradoId = poliza.AseguradoId,
            InicioVigencia = poliza.InicioVigencia, FinVigencia = poliza.FinVigencia,
            Coberturas = poliza.Coberturas.Select(c => new CoberturaFila
            {
                PolizaId = poliza.Id, Codigo = c.Codigo.ToString(), SumaAsegurada = c.SumaAsegurada,
                TipoDeDeducible = c.Deducible.Tipo.ToString(), ValorDeDeducible = c.Deducible.Valor,
                Coaseguro = c.Coaseguro,
            }).ToList(),
        };
        db.Polizas.Add(fila);
        _rastreados[poliza.Id] = new Rastreo(poliza, fila, []);
    }

    // ── Ajustadores ─────────────────────────────────────────────────────────────────────────────────────────────
    async Task<Ajustador?> IAjustadores.ObtenerAsync(Guid id, CancellationToken ct)
    {
        if (_rastreados.TryGetValue(id, out var r))
        {
            return (Ajustador)r.Agregado;
        }

        var fila = await db.Ajustadores.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (fila is null)
        {
            return null;
        }

        var a = Ajustador.Rehidratar(fila.Id, fila.Nombre, fila.Especialidades.Select(Enum.Parse<Especialidad>),
            fila.Activo);
        _rastreados[id] = new Rastreo(a, fila, []);
        return a;
    }

    /// <inheritdoc />
    public Task<int> CasosAbiertosAsync(Guid ajustadorId, CancellationToken ct) =>
        db.Siniestros.AsNoTracking().CountAsync(s => s.AjustadorId == ajustadorId &&
            s.Estado != nameof(EstadoSiniestro.Pagado) && s.Estado != nameof(EstadoSiniestro.Rechazado), ct);

    /// <inheritdoc />
    public void Agregar(Ajustador ajustador)
    {
        var fila = new AjustadorFila { Id = ajustador.Id, Nombre = ajustador.Nombre };
        db.Ajustadores.Add(fila);
        _rastreados[ajustador.Id] = new Rastreo(ajustador, fila, []);
    }

    // ── Siniestros ──────────────────────────────────────────────────────────────────────────────────────────────
    async Task<Siniestro?> ISiniestros.ObtenerAsync(Guid id, CancellationToken ct) =>
        _rastreados.TryGetValue(id, out var r)
            ? (Siniestro)r.Agregado
            : Rastrear(await db.Siniestros.Include(s => s.Movimientos).FirstOrDefaultAsync(s => s.Id == id, ct));

    /// <inheritdoc />
    public Task<int> ContarDeLaPolizaAsync(Guid polizaId, Guid excepto, DateOnly desde, DateOnly hasta,
        CancellationToken ct) =>
        db.Siniestros.AsNoTracking().CountAsync(s => s.PolizaId == polizaId && s.Id != excepto &&
                                                     s.FechaOcurrencia >= desde && s.FechaOcurrencia <= hasta, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Siniestro>> DelAseguradoAsync(Guid aseguradoId, CancellationToken ct)
    {
        var filas = await db.Siniestros.Include(s => s.Movimientos).Where(s => s.AseguradoId == aseguradoId)
            .OrderBy(s => s.FechaAviso).ThenBy(s => s.Id).ToListAsync(ct);
        return filas.Select(f => _rastreados.TryGetValue(f.Id, out var r) ? (Siniestro)r.Agregado : Rastrear(f)!)
            .ToList();
    }

    /// <inheritdoc />
    public void Agregar(Siniestro siniestro)
    {
        var fila = new SiniestroFila { Id = siniestro.Id };
        db.Siniestros.Add(fila);
        _rastreados[siniestro.Id] = new Rastreo(siniestro, fila, []);
    }

    // ── Propuestas (documentos) ─────────────────────────────────────────────────────────────────────────────────
    /// <inheritdoc />
    public void Agregar(PropuestaDeTriaje propuesta) => db.Propuestas.Add(new PropuestaFila
    {
        Id = propuesta.Id, SiniestroId = propuesta.SiniestroId, GeneradaEn = propuesta.GeneradaEn,
        Documento = JsonSerializer.Serialize(propuesta, Json),
    });

    async Task<PropuestaDeTriaje?> IPropuestasDeTriaje.ObtenerAsync(Guid id, CancellationToken ct) =>
        await db.Propuestas.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) is { } fila
            ? JsonSerializer.Deserialize<PropuestaDeTriaje>(fila.Documento, Json)
            : null;

    // ── Unidad de trabajo ───────────────────────────────────────────────────────────────────────────────────────
    /// <inheritdoc />
    public async Task ConfirmarAsync(CancellationToken ct)
    {
        foreach (var r in _rastreados.Values)
        {
            Aplicar(r);
            foreach (var evento in r.Agregado.TomarEventos())
            {
                var (tipo, contenido) = SerializadorDeEventos.Escribir(evento);
                db.Salida.Add(new MensajeDeSalida
                {
                    Id = evento.Id, Tipo = tipo, Agregado = r.Agregado.Id, Contenido = contenido,
                    OcurridoEn = evento.OcurridoEn,
                });
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new ConflictoDeConcurrencia("Otro cambio guardó el mismo registro primero; vuelva a consultarlo.", e);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictoDeConcurrencia("Ya existe un registro con esa llave.", e);
        }

        foreach (var r in _rastreados.Values)
        {
            Recordar(r);
        }
    }

    private void Aplicar(Rastreo r)
    {
        switch (r.Agregado, r.Fila)
        {
            case (Asegurado a, AseguradoFila f):
                f.Nombre = a.Nombre;
                f.AnonimizadoEn = a.AnonimizadoEn;
                f.RfcCifrado = Cifrar(r, "rfc", a.Rfc, f.RfcCifrado, "asegurados.rfc");
                f.CorreoCifrado = Cifrar(r, "correo", a.Correo, f.CorreoCifrado, "asegurados.correo");
                f.TelefonoCifrado = Cifrar(r, "telefono", a.Telefono, f.TelefonoCifrado, "asegurados.telefono");
                f.ClabeCifrada = Cifrar(r, "clabe", a.Clabe, f.ClabeCifrada, "asegurados.clabe");
                break;
            case (Poliza p, PolizaFila f):
                f.PrimaPagadaHasta = p.PrimaPagadaHasta;
                f.FechaCancelacion = p.FechaCancelacion;
                break;
            case (Ajustador a, AjustadorFila f):
                Lista(f.Especialidades, a.Especialidades.Order().Select(x => x.ToString()), v => f.Especialidades = v);
                f.Activo = a.Activo;
                break;
            case (Siniestro s, SiniestroFila f):
                AplicarSiniestro(r, s.Instantanea(), f);
                break;
            default:
                throw new InvalidOperationException($"Agregado sin mapeo: {r.Agregado.GetType().Name}");
        }
    }

    private void AplicarSiniestro(Rastreo r, EstadoGuardado e, SiniestroFila f)
    {
        f.PolizaId = e.PolizaId;
        f.AseguradoId = e.AseguradoId;
        f.Ramo = e.Ramo.ToString();
        f.FechaOcurrencia = e.FechaOcurrencia;
        f.FechaAviso = e.FechaAviso;
        f.DescripcionCifrada = Cifrar(r, "descripcion", e.Descripcion, f.DescripcionCifrada, "siniestros.descripcion");
        f.LesionesCifradas = Cifrar(r, "lesiones", e.DetalleDeLesiones, f.LesionesCifradas, "siniestros.lesiones");
        f.Estimacion = e.Estimacion;
        f.Estado = e.Estado.ToString();
        f.Tipo = e.Tipo?.ToString();
        f.Cobertura = e.Cobertura?.ToString();
        f.MotivoNoElegible = e.MotivoNoElegible?.ToString();
        Lista(f.Indicios, e.Indicios.Order().Select(x => x.ToString()), v => f.Indicios = v);
        f.TriadoPor = e.TriadoPor;
        f.PropuestaAceptada = e.PropuestaAceptada;
        f.AjustadorId = e.AjustadorId;
        Lista(f.DatosRecibidos, e.DatosRecibidos.Order().Select(x => x.ToString()), v => f.DatosRecibidos = v);
        f.DictamenProcedente = e.Dictamen?.Procedente;
        f.DictamenMotivo = e.Dictamen?.Motivo.ToString();
        f.DictamenClausula = e.Dictamen?.Clausula;
        f.PerdidaAjustada = e.Dictamen?.PerdidaAjustada;
        f.Indemnizacion = e.Dictamen?.Indemnizacion;
        f.FechaDictamen = e.Dictamen?.Fecha;
        f.DictaminadoPor = e.Dictamen?.DictaminadoPor;
        Lista(f.ExclusionesConfirmadas, e.Dictamen?.Exclusiones.Select(x => x.ToString()) ?? [],
            v => f.ExclusionesConfirmadas = v);
        f.FechaLimiteDePago = e.FechaLimiteDePago;
        f.PagoReferencia = e.Pago?.Referencia;
        f.PagoMonto = e.Pago?.Monto;
        f.FechaPago = e.Pago?.Fecha;
        f.PagadoPor = e.Pago?.PagadoPor;
        f.PagoFueraDePlazo = e.Pago?.FueraDePlazo;
        f.FechaRechazo = e.FechaRechazo;
        for (var i = f.Movimientos.Count; i < e.Movimientos.Count; i++)
        {
            var m = e.Movimientos[i];
            f.Movimientos.Add(new MovimientoDeReservaFila
            {
                SiniestroId = e.Id, Secuencia = i + 1, Fecha = m.Fecha, Tipo = m.Tipo.ToString(), Monto = m.Monto,
                Saldo = m.Saldo, Motivo = m.Motivo,
            });
        }
    }

    private Poliza? Rastrear(PolizaFila? f)
    {
        if (f is null)
        {
            return null;
        }

        var p = Poliza.Rehidratar(f.Id, f.Folio, Enum.Parse<Ramo>(f.Ramo), f.AseguradoId, f.InicioVigencia,
            f.FinVigencia, f.PrimaPagadaHasta, f.FechaCancelacion, f.Coberturas.OrderBy(c => c.Codigo).Select(c =>
                new CoberturaContratada(Enum.Parse<CodigoCobertura>(c.Codigo), c.SumaAsegurada,
                    new Deducible(Enum.Parse<TipoDeDeducible>(c.TipoDeDeducible), c.ValorDeDeducible), c.Coaseguro)));
        _rastreados[p.Id] = new Rastreo(p, f, []);
        return p;
    }

    private Siniestro? Rastrear(SiniestroFila? f)
    {
        if (f is null)
        {
            return null;
        }

        var claros = new Dictionary<string, string?>
        {
            ["descripcion"] = Descifrar(f.DescripcionCifrada, "siniestros.descripcion"),
            ["lesiones"] = Descifrar(f.LesionesCifradas, "siniestros.lesiones"),
        };
        f.Movimientos = f.Movimientos.OrderBy(m => m.Secuencia).ToList();
        var dictamen = f.DictamenProcedente is { } procedente
            ? new Dictamen(procedente, Enum.Parse<MotivoDeDictamen>(f.DictamenMotivo!), f.DictamenClausula,
                f.PerdidaAjustada!.Value, f.Indemnizacion!.Value, f.FechaDictamen!.Value, f.DictaminadoPor!,
                f.ExclusionesConfirmadas.Select(Enum.Parse<CodigoExclusion>).ToList())
            : null;
        var pago = f.PagoReferencia is { } referencia
            ? new Pago(referencia, f.PagoMonto!.Value, f.FechaPago!.Value, f.PagadoPor!, f.PagoFueraDePlazo!.Value)
            : null;
        var s = Siniestro.Rehidratar(new EstadoGuardado(f.Id, f.PolizaId, f.AseguradoId, Enum.Parse<Ramo>(f.Ramo),
            f.FechaOcurrencia, f.FechaAviso, claros["descripcion"], claros["lesiones"], f.Estimacion,
            Enum.Parse<EstadoSiniestro>(f.Estado), Parsear<TipoDeSiniestro>(f.Tipo), Parsear<CodigoCobertura>(f.Cobertura),
            Parsear<MotivoDeNoElegibilidad>(f.MotivoNoElegible), f.Indicios.Select(Enum.Parse<IndicioDeFraude>).ToList(),
            f.TriadoPor, f.PropuestaAceptada, f.AjustadorId, f.DatosRecibidos.Select(Enum.Parse<DatoRequerido>).ToList(),
            f.Movimientos.Select(m => new MovimientoDeReserva(m.Fecha, Enum.Parse<TipoDeMovimiento>(m.Tipo), m.Monto,
                m.Saldo, m.Motivo)).ToList(), dictamen, f.FechaLimiteDePago, pago, f.FechaRechazo));
        _rastreados[s.Id] = new Rastreo(s, f, claros);
        return s;
    }

    private static T? Parsear<T>(string? valor) where T : struct, Enum => valor is null ? null : Enum.Parse<T>(valor);

    private string? Descifrar(byte[]? datos, string campo) => datos is null ? null : cifrador.Descifrar(datos, campo);

    /// <summary>Cifra sólo si el texto claro cambió: el nonce es aleatorio y re-cifrar igual reescribiría la fila.</summary>
    private byte[]? Cifrar(Rastreo r, string llave, string? claro, byte[]? actual, string campo)
    {
        if (r.Claros.TryGetValue(llave, out var anterior) && anterior == claro)
        {
            return actual;
        }

        return claro is null ? null : cifrador.Cifrar(claro, campo);
    }

    private static void Recordar(Rastreo r)
    {
        switch (r.Agregado)
        {
            case Asegurado a:
                r.Claros["rfc"] = a.Rfc;
                r.Claros["correo"] = a.Correo;
                r.Claros["telefono"] = a.Telefono;
                r.Claros["clabe"] = a.Clabe;
                break;
            case Siniestro s:
                r.Claros["descripcion"] = s.Descripcion;
                r.Claros["lesiones"] = s.DetalleDeLesiones;
                break;
        }
    }

    private static void Lista(List<string> actual, IEnumerable<string> nueva, Action<List<string>> asignar)
    {
        var lista = nueva.ToList();
        if (!actual.SequenceEqual(lista))
        {
            asignar(lista);
        }
    }
}

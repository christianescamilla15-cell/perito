using Perito.Domain.Ajustadores;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;

namespace Perito.Domain.Siniestros;

/// <summary>Estados del siniestro (docs/PLAN.md).</summary>
public enum EstadoSiniestro
{
    /// <summary>Aviso recibido (FNOL).</summary>
    Avisado,

    /// <summary>Tipo, cobertura, elegibilidad e indicios calculados.</summary>
    Triado,

    /// <summary>Con ajustador.</summary>
    Asignado,

    /// <summary>Con reserva constituida.</summary>
    Reservado,

    /// <summary>Con dictamen.</summary>
    Dictaminado,

    /// <summary>Indemnización pagada (final).</summary>
    Pagado,

    /// <summary>Rechazo comunicado (final).</summary>
    Rechazado,
}

/// <summary>Indicios de fraude por reglas sobre datos estructurados (GEN-9).</summary>
public enum IndicioDeFraude
{
    /// <summary>Dentro de los primeros 15 días de vigencia.</summary>
    SiniestroTemprano,

    /// <summary>Aviso después del plazo de 5 días.</summary>
    AvisoTardio,

    /// <summary>Dos o más siniestros en los 12 meses anteriores.</summary>
    FrecuenciaAlta,

    /// <summary>Estimación del 90 % o más de la suma asegurada.</summary>
    MontoCercanoASuma,
}

/// <summary>Resultado de la investigación especial.</summary>
public enum ResultadoDeInvestigacion
{
    /// <summary>No se encontró fraude.</summary>
    SinHallazgos,

    /// <summary>Fraude comprobado (GEN-9).</summary>
    FraudeComprobado,
}

/// <summary>Por qué se dictaminó así.</summary>
public enum MotivoDeDictamen
{
    /// <summary>Procede el pago.</summary>
    Procedente,

    /// <summary>GEN-2.</summary>
    FueraDeVigencia,

    /// <summary>GEN-2.</summary>
    PolizaCancelada,

    /// <summary>GEN-3.</summary>
    PrimaVencida,

    /// <summary>GEN-1.</summary>
    CoberturaNoContratada,

    /// <summary>GEN-9.</summary>
    Fraude,

    /// <summary>Una exclusión confirmada.</summary>
    Exclusion,

    /// <summary>GEN-6.</summary>
    PerdidaMenorAlDeducible,
}

/// <summary>Lo que el ajustador aporta para dictaminar: hechos verificados, nunca la decisión.</summary>
public sealed record EntradaDeDictamen(
    decimal PerdidaAjustada, IReadOnlyCollection<CodigoExclusion> ExclusionesConfirmadas,
    ResultadoDeInvestigacion? Investigacion);

/// <summary>El dictamen que calcula el dominio.</summary>
public sealed record Dictamen(
    bool Procedente, MotivoDeDictamen Motivo, string? Clausula, decimal PerdidaAjustada, decimal Indemnizacion,
    DateOnly Fecha, string DictaminadoPor, IReadOnlyList<CodigoExclusion> Exclusiones);

/// <summary>Tipo de movimiento de reserva.</summary>
public enum TipoDeMovimiento
{
    /// <summary>Reserva inicial.</summary>
    Constitucion,

    /// <summary>Cambio de la reserva.</summary>
    Ajuste,

    /// <summary>La reserva se libera al pagar o rechazar.</summary>
    Liberacion,
}

/// <summary>Un movimiento de reserva con el saldo que deja.</summary>
public sealed record MovimientoDeReserva(
    DateTimeOffset Fecha, TipoDeMovimiento Tipo, decimal Monto, decimal Saldo, string Motivo);

/// <summary>El pago de la indemnización.</summary>
public sealed record Pago(string Referencia, decimal Monto, DateOnly Fecha, string PagadoPor, bool FueraDePlazo);

/// <summary>Instantánea de un siniestro guardado, para rehidratarlo sin pasar por las reglas.</summary>
public sealed record EstadoGuardado(
    Guid Id, Guid PolizaId, Guid AseguradoId, Ramo Ramo, DateOnly FechaOcurrencia, DateOnly FechaAviso,
    string? Descripcion, string? DetalleDeLesiones, decimal? Estimacion, EstadoSiniestro Estado,
    TipoDeSiniestro? Tipo, CodigoCobertura? Cobertura, MotivoDeNoElegibilidad? MotivoNoElegible,
    IReadOnlyCollection<IndicioDeFraude> Indicios, string? TriadoPor, Guid? PropuestaAceptada, Guid? AjustadorId,
    IReadOnlyCollection<DatoRequerido> DatosRecibidos, IReadOnlyList<MovimientoDeReserva> Movimientos,
    Dictamen? Dictamen, DateOnly? FechaLimiteDePago, Pago? Pago, DateOnly? FechaRechazo);

/// <summary>
/// Siniestro: la máquina de estados del flujo. Toda transición pasa por una regla y devuelve un error con nombre si no
/// procede. Las propuestas del asistente NO llegan aquí: el triaje lo pide una persona con un tipo, y el dominio
/// calcula cobertura, elegibilidad, indicios, reserva e indemnización por su cuenta.
/// </summary>
public sealed class Siniestro : RaizDeAgregado
{
    /// <summary>Días naturales para avisar (GEN-4).</summary>
    public const int PlazoDeAvisoDias = 5;

    /// <summary>Primeros días de vigencia que cuentan como siniestro temprano (GEN-9).</summary>
    public const int DiasDeSiniestroTemprano = 15;

    /// <summary>Siniestros previos en 12 meses a partir de los cuales hay indicio (GEN-9).</summary>
    public const int UmbralDeFrecuencia = 2;

    /// <summary>Fracción de la suma asegurada a partir de la cual la estimación es indicio (GEN-9).</summary>
    public const decimal UmbralDeMonto = 0.9m;

    /// <summary>Indicios que obligan a investigación especial (GEN-9).</summary>
    public const int IndiciosParaInvestigar = 2;

    /// <summary>Días naturales para pagar desde el dictamen (GEN-10).</summary>
    public const int PlazoDePagoDias = 30;

    private readonly HashSet<IndicioDeFraude> _indicios;
    private readonly HashSet<DatoRequerido> _datos;
    private readonly List<MovimientoDeReserva> _movimientos;

    private Siniestro(EstadoGuardado e)
    {
        Id = e.Id;
        PolizaId = e.PolizaId;
        AseguradoId = e.AseguradoId;
        Ramo = e.Ramo;
        FechaOcurrencia = e.FechaOcurrencia;
        FechaAviso = e.FechaAviso;
        Descripcion = e.Descripcion;
        DetalleDeLesiones = e.DetalleDeLesiones;
        Estimacion = e.Estimacion;
        Estado = e.Estado;
        Tipo = e.Tipo;
        Cobertura = e.Cobertura;
        MotivoNoElegible = e.MotivoNoElegible;
        _indicios = e.Indicios.ToHashSet();
        TriadoPor = e.TriadoPor;
        PropuestaAceptada = e.PropuestaAceptada;
        AjustadorId = e.AjustadorId;
        _datos = e.DatosRecibidos.ToHashSet();
        _movimientos = e.Movimientos.ToList();
        Dictamen = e.Dictamen;
        FechaLimiteDePago = e.FechaLimiteDePago;
        Pago = e.Pago;
        FechaRechazo = e.FechaRechazo;
    }

    /// <summary>Póliza afectada.</summary>
    public Guid PolizaId { get; }

    /// <summary>Titular (sólo su identificador).</summary>
    public Guid AseguradoId { get; }

    /// <summary>Ramo de la póliza.</summary>
    public Ramo Ramo { get; }

    /// <summary>Día en que ocurrió.</summary>
    public DateOnly FechaOcurrencia { get; }

    /// <summary>Día del aviso.</summary>
    public DateOnly FechaAviso { get; }

    /// <summary>Descripción libre (dato personal posible: se cifra en reposo y se borra al anonimizar).</summary>
    public string? Descripcion { get; private set; }

    /// <summary>Detalle de lesiones (dato sensible de salud: cifrado en reposo).</summary>
    public string? DetalleDeLesiones { get; private set; }

    /// <summary>Estimación inicial de la pérdida, si el asegurado la dio.</summary>
    public decimal? Estimacion { get; }

    /// <summary>Estado actual.</summary>
    public EstadoSiniestro Estado { get; private set; }

    /// <summary>Tipo decidido en el triaje.</summary>
    public TipoDeSiniestro? Tipo { get; private set; }

    /// <summary>Cobertura que lo ampara según el catálogo.</summary>
    public CodigoCobertura? Cobertura { get; private set; }

    /// <summary>Motivo de no elegibilidad, si lo hay.</summary>
    public MotivoDeNoElegibilidad? MotivoNoElegible { get; private set; }

    /// <summary>Verdadero si ya se trió y es elegible.</summary>
    public bool EsElegible => Tipo is not null && MotivoNoElegible is null;

    /// <summary>Indicios de fraude calculados en el triaje.</summary>
    public IReadOnlySet<IndicioDeFraude> Indicios => _indicios;

    /// <summary>Si los indicios obligan a investigación especial.</summary>
    public bool RequiereInvestigacion => _indicios.Count >= IndiciosParaInvestigar;

    /// <summary>Quién trió.</summary>
    public string? TriadoPor { get; private set; }

    /// <summary>La propuesta del asistente que la persona tomó como base, si alguna (auditoría).</summary>
    public Guid? PropuestaAceptada { get; private set; }

    /// <summary>Ajustador asignado.</summary>
    public Guid? AjustadorId { get; private set; }

    /// <summary>Datos y documentos recibidos.</summary>
    public IReadOnlySet<DatoRequerido> DatosRecibidos => _datos;

    /// <summary>Movimientos de reserva.</summary>
    public IReadOnlyList<MovimientoDeReserva> Movimientos => _movimientos;

    /// <summary>Saldo de la reserva.</summary>
    public decimal Reserva => _movimientos.Count == 0 ? 0m : _movimientos[^1].Saldo;

    /// <summary>Dictamen, si ya lo hay.</summary>
    public Dictamen? Dictamen { get; private set; }

    /// <summary>Fecha límite para pagar un dictamen procedente (GEN-10).</summary>
    public DateOnly? FechaLimiteDePago { get; private set; }

    /// <summary>El pago, si ya se hizo.</summary>
    public Pago? Pago { get; private set; }

    /// <summary>Fecha en que se comunicó el rechazo.</summary>
    public DateOnly? FechaRechazo { get; private set; }

    /// <summary>Datos que el tipo exige y todavía no se reciben.</summary>
    public IReadOnlyList<DatoRequerido> DatosPendientes =>
        Tipo is { } t ? Catalogo.De(t).Datos.Where(d => !_datos.Contains(d)).ToList() : [];

    /// <summary>
    /// Aviso de siniestro (FNOL). Valida fechas (GEN-4: la ocurrencia no es posterior al aviso) y la descripción.
    /// No mira la vigencia: un aviso sobre una póliza vencida se registra y se rechaza por escrito después.
    /// </summary>
    public static Resultado<Siniestro> Avisar(Guid id, Poliza poliza, DateOnly ocurrencia, DateOnly aviso,
        string descripcion, decimal? estimacion, string? detalleDeLesiones, DateTimeOffset ahora)
    {
        if (ocurrencia > aviso)
        {
            return Resultado<Siniestro>.Fallo("ocurrencia_futura",
                "La fecha de ocurrencia no puede ser posterior a la del aviso.");
        }

        descripcion = (descripcion ?? string.Empty).Trim();
        if (descripcion.Length is < 20 or > 4000)
        {
            return Resultado<Siniestro>.Fallo("descripcion_invalida", "La descripción tiene de 20 a 4000 caracteres.");
        }

        if (estimacion is <= 0)
        {
            return Resultado<Siniestro>.Fallo("estimacion_invalida", "La estimación, si se da, es positiva.");
        }

        if (detalleDeLesiones is { Length: > 2000 })
        {
            return Resultado<Siniestro>.Fallo("lesiones_invalidas", "El detalle de lesiones tiene hasta 2000 caracteres.");
        }

        var siniestro = new Siniestro(new EstadoGuardado(id, poliza.Id, poliza.AseguradoId, poliza.Ramo, ocurrencia,
            aviso, descripcion, string.IsNullOrWhiteSpace(detalleDeLesiones) ? null : detalleDeLesiones.Trim(),
            estimacion, EstadoSiniestro.Avisado, null, null, null, [], null, null, null, [], [], null, null, null,
            null));
        siniestro.Registrar(new SiniestroAvisado(Guid.CreateVersion7(ahora), ahora, id, poliza.Id, poliza.Ramo));
        return Resultado<Siniestro>.Exito(siniestro);
    }

    /// <summary>Reconstruye un siniestro guardado.</summary>
    public static Siniestro Rehidratar(EstadoGuardado estado) => new(estado);

    /// <summary>Instantánea para guardar.</summary>
    public EstadoGuardado Instantanea() => new(Id, PolizaId, AseguradoId, Ramo, FechaOcurrencia, FechaAviso,
        Descripcion, DetalleDeLesiones, Estimacion, Estado, Tipo, Cobertura, MotivoNoElegible, _indicios.ToList(),
        TriadoPor, PropuestaAceptada, AjustadorId, _datos.ToList(), _movimientos.ToList(), Dictamen,
        FechaLimiteDePago, Pago, FechaRechazo);

    /// <summary>
    /// Triaje: una persona fija el tipo; el dominio deriva la cobertura del catálogo y calcula elegibilidad (GEN-1/2/3)
    /// e indicios de fraude (GEN-4 y GEN-9). Si el tipo vino de una propuesta del asistente se registra cuál, pero el
    /// resultado es el mismo que sin ella.
    /// </summary>
    public Resultado Triar(TipoDeSiniestro tipo, Poliza poliza, int siniestrosPreviosEn12Meses, string usuario,
        Guid? propuestaAceptada, DateTimeOffset ahora)
    {
        if (Estado != EstadoSiniestro.Avisado)
        {
            return Resultado.Fallo(Error.TransicionInvalida("triar", Estado));
        }

        if (poliza.Id != PolizaId)
        {
            return Resultado.Fallo("poliza_distinta", "La póliza no es la del siniestro.");
        }

        var definicion = Catalogo.De(tipo);
        if (definicion.Ramo != Ramo)
        {
            return Resultado.Fallo("tipo_de_otro_ramo", $"El tipo {tipo} no es del ramo {Ramo}.");
        }

        var elegibilidad = poliza.Elegibilidad(FechaOcurrencia, definicion.Cobertura);
        _indicios.Clear();
        if (FechaOcurrencia.DayNumber - poliza.InicioVigencia.DayNumber is >= 0 and < DiasDeSiniestroTemprano)
        {
            _indicios.Add(IndicioDeFraude.SiniestroTemprano);
        }

        if (FechaAviso.DayNumber - FechaOcurrencia.DayNumber > PlazoDeAvisoDias)
        {
            _indicios.Add(IndicioDeFraude.AvisoTardio);
        }

        if (siniestrosPreviosEn12Meses >= UmbralDeFrecuencia)
        {
            _indicios.Add(IndicioDeFraude.FrecuenciaAlta);
        }

        if (Estimacion is { } estimacion && elegibilidad.Cobertura is { } cobertura &&
            estimacion >= cobertura.SumaAsegurada * UmbralDeMonto)
        {
            _indicios.Add(IndicioDeFraude.MontoCercanoASuma);
        }

        Tipo = tipo;
        Cobertura = definicion.Cobertura;
        MotivoNoElegible = elegibilidad.Motivo;
        TriadoPor = usuario;
        PropuestaAceptada = propuestaAceptada;
        Estado = EstadoSiniestro.Triado;
        Registrar(new SiniestroTriado(Guid.CreateVersion7(ahora), ahora, Id, Ramo, tipo, definicion.Cobertura,
            elegibilidad.EsElegible, RequiereInvestigacion));
        return Resultado.Exito();
    }

    /// <summary>Asigna un ajustador que cumpla especialidad, investigación y carga.</summary>
    public Resultado AsignarAjustador(Ajustador ajustador, int casosAbiertosDelAjustador, DateTimeOffset ahora)
    {
        if (Estado != EstadoSiniestro.Triado)
        {
            return Resultado.Fallo(Error.TransicionInvalida("asignar", Estado));
        }

        var puede = ajustador.PuedeAtender(Ramo, RequiereInvestigacion, casosAbiertosDelAjustador);
        if (!puede.EsExito)
        {
            return puede;
        }

        AjustadorId = ajustador.Id;
        Estado = EstadoSiniestro.Asignado;
        Registrar(new AjustadorAsignado(Guid.CreateVersion7(ahora), ahora, Id, ajustador.Id));
        return Resultado.Exito();
    }

    /// <summary>Registra un dato o documento recibido; sólo los que pide el tipo (GEN-8). Repetirlo no cambia nada.</summary>
    public Resultado RegistrarDatoRecibido(DatoRequerido dato)
    {
        if (Estado is not (EstadoSiniestro.Asignado or EstadoSiniestro.Reservado))
        {
            return Resultado.Fallo(Error.TransicionInvalida("registrar datos de", Estado));
        }

        if (!Catalogo.De(Tipo!.Value).Datos.Contains(dato))
        {
            return Resultado.Fallo("dato_no_requerido", $"El tipo {Tipo} no pide {dato}.");
        }

        _datos.Add(dato);
        return Resultado.Exito();
    }

    /// <summary>
    /// Constituye la reserva con la pérdida estimada NETA: el mismo cálculo de la indemnización (GEN-7) sobre la
    /// estimación, con tope en la suma asegurada (GEN-5). Sólo para siniestros elegibles.
    /// </summary>
    public Resultado ConstituirReserva(Poliza poliza, decimal estimacion, DateTimeOffset ahora)
    {
        if (Estado != EstadoSiniestro.Asignado)
        {
            return Resultado.Fallo(Error.TransicionInvalida("reservar", Estado));
        }

        if (!EsElegible)
        {
            return Resultado.Fallo("sin_elegibilidad", $"No elegible ({MotivoNoElegible}): se dictamina sin reserva.");
        }

        if (estimacion <= 0)
        {
            return Resultado.Fallo("estimacion_invalida", "La estimación es positiva.");
        }

        var monto = CalculoDeIndemnizacion.Calcular(estimacion, poliza.Cobertura(Cobertura!.Value)!);
        if (monto == 0)
        {
            return Resultado.Fallo("estimacion_menor_al_deducible",
                "La estimación no supera el deducible: se dictamina improcedente sin reserva.");
        }

        _movimientos.Add(new MovimientoDeReserva(ahora, TipoDeMovimiento.Constitucion, monto, monto, "constitución"));
        Estado = EstadoSiniestro.Reservado;
        Registrar(new ReservaConstituida(Guid.CreateVersion7(ahora), ahora, Id, Ramo, Cobertura.Value, monto));
        return Resultado.Exito();
    }

    /// <summary>Ajusta la reserva a un nuevo saldo positivo que no excede la suma asegurada, con su motivo.</summary>
    public Resultado AjustarReserva(Poliza poliza, decimal nuevoSaldo, string motivo, DateTimeOffset ahora)
    {
        if (Estado != EstadoSiniestro.Reservado)
        {
            return Resultado.Fallo(Error.TransicionInvalida("ajustar la reserva de", Estado));
        }

        var suma = poliza.Cobertura(Cobertura!.Value)!.SumaAsegurada;
        nuevoSaldo = Pesos.Redondear(nuevoSaldo);
        if (nuevoSaldo <= 0 || nuevoSaldo > suma)
        {
            return Resultado.Fallo("reserva_invalida", $"La reserva va de 0.01 a la suma asegurada ({suma:0.00}).");
        }

        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
        {
            return Resultado.Fallo("motivo_requerido", "Todo ajuste de reserva lleva su motivo.");
        }

        var delta = nuevoSaldo - Reserva;
        if (delta == 0)
        {
            return Resultado.Fallo("reserva_sin_cambio", "El nuevo saldo es igual al actual.");
        }

        _movimientos.Add(new MovimientoDeReserva(ahora, TipoDeMovimiento.Ajuste, delta, nuevoSaldo, motivo.Trim()));
        Registrar(new ReservaAjustada(Guid.CreateVersion7(ahora), ahora, Id, Ramo, delta, nuevoSaldo));
        return Resultado.Exito();
    }

    /// <summary>
    /// Dictamen determinista. Orden: elegibilidad (GEN-1/2/3) → fraude comprobado (GEN-9) → exclusiones confirmadas
    /// que aplican a la cobertura → indemnización (GEN-5/6/7). Un dictamen procedente exige reserva suficiente; uno
    /// improcedente no necesita reserva. Con indicios, exige el resultado de la investigación; si es elegible, exige la
    /// documentación del tipo (GEN-8).
    /// </summary>
    public Resultado Dictaminar(Poliza poliza, EntradaDeDictamen entrada, string usuario, DateOnly fecha,
        DateTimeOffset ahora)
    {
        if (Estado is not (EstadoSiniestro.Asignado or EstadoSiniestro.Reservado))
        {
            return Resultado.Fallo(Error.TransicionInvalida("dictaminar", Estado));
        }

        if (entrada.PerdidaAjustada < 0)
        {
            return Resultado.Fallo("perdida_invalida", "La pérdida ajustada no es negativa.");
        }

        var noAplican = entrada.ExclusionesConfirmadas
            .Where(x => !Catalogo.De(x).Aplica.Contains(Cobertura!.Value)).ToList();
        if (noAplican.Count > 0)
        {
            return Resultado.Fallo("exclusion_no_aplica",
                $"{string.Join(", ", noAplican)} no aplica a la cobertura {Cobertura}.");
        }

        if (RequiereInvestigacion && entrada.Investigacion is null)
        {
            return Resultado.Fallo("investigacion_pendiente",
                "Los indicios obligan a investigación especial antes de dictaminar (GEN-9).");
        }

        if (EsElegible && DatosPendientes.Count > 0)
        {
            return Resultado.Fallo("documentacion_incompleta",
                $"Falta documentación (GEN-8): {string.Join(", ", DatosPendientes)}.");
        }

        var exclusiones = entrada.ExclusionesConfirmadas.Distinct().Order().ToList();
        var (motivo, clausula, indemnizacion) = Decidir(poliza, entrada, exclusiones);
        var procedente = motivo == MotivoDeDictamen.Procedente;
        if (procedente && Estado != EstadoSiniestro.Reservado)
        {
            return Resultado.Fallo("reserva_requerida", "Un dictamen procedente necesita la reserva constituida.");
        }

        if (procedente && indemnizacion > Reserva)
        {
            return Resultado.Fallo("reserva_insuficiente",
                $"La indemnización ({indemnizacion:0.00}) excede la reserva ({Reserva:0.00}): ajústela antes.");
        }

        Dictamen = new Dictamen(procedente, motivo, clausula, entrada.PerdidaAjustada, indemnizacion, fecha, usuario,
            exclusiones);
        FechaLimiteDePago = procedente ? fecha.AddDays(PlazoDePagoDias) : null;
        Estado = EstadoSiniestro.Dictaminado;
        Registrar(new SiniestroDictaminado(Guid.CreateVersion7(ahora), ahora, Id, Ramo, procedente, motivo,
            indemnizacion));
        return Resultado.Exito();
    }

    /// <summary>
    /// Paga un dictamen procedente. Separación de funciones: quien dictaminó no paga. Pagar después de la fecha límite
    /// se permite pero queda marcado (GEN-10). La reserva se libera.
    /// </summary>
    public Resultado Pagar(string referencia, string usuario, DateOnly fecha, DateTimeOffset ahora)
    {
        if (Estado != EstadoSiniestro.Dictaminado || Dictamen is not { Procedente: true } dictamen)
        {
            return Resultado.Fallo(Error.TransicionInvalida("pagar", Estado));
        }

        if (string.Equals(usuario, dictamen.DictaminadoPor, StringComparison.Ordinal))
        {
            return Resultado.Fallo("separacion_de_funciones", "Quien dictamina no puede pagar el mismo siniestro.");
        }

        if (string.IsNullOrWhiteSpace(referencia) || referencia.Length > 60)
        {
            return Resultado.Fallo("referencia_invalida", "La referencia del pago tiene de 1 a 60 caracteres.");
        }

        Pago = new Pago(referencia.Trim(), dictamen.Indemnizacion, fecha, usuario, fecha > FechaLimiteDePago);
        var liberada = Liberar("pago", ahora);
        Estado = EstadoSiniestro.Pagado;
        Registrar(new PagoEmitido(Guid.CreateVersion7(ahora), ahora, Id, Ramo, dictamen.Indemnizacion, liberada));
        return Resultado.Exito();
    }

    /// <summary>Comunica el rechazo de un dictamen improcedente con su causa y cláusula (GEN-11); libera la reserva.</summary>
    public Resultado Rechazar(DateOnly fecha, DateTimeOffset ahora)
    {
        if (Estado != EstadoSiniestro.Dictaminado || Dictamen is not { Procedente: false } dictamen)
        {
            return Resultado.Fallo(Error.TransicionInvalida("rechazar", Estado));
        }

        FechaRechazo = fecha;
        var liberada = Liberar("rechazo", ahora);
        Estado = EstadoSiniestro.Rechazado;
        Registrar(new SiniestroRechazado(Guid.CreateVersion7(ahora), ahora, Id, Ramo, dictamen.Motivo,
            dictamen.Clausula, liberada));
        return Resultado.Exito();
    }

    /// <summary>Borra el texto libre al anonimizar al titular; montos, fechas y estados se conservan.</summary>
    public void BorrarTextoLibre()
    {
        Descripcion = null;
        DetalleDeLesiones = null;
    }

    /// <summary>Abierto: todavía no se paga ni se rechaza.</summary>
    public bool EstaAbierto => Estado is not (EstadoSiniestro.Pagado or EstadoSiniestro.Rechazado);

    private (MotivoDeDictamen Motivo, string? Clausula, decimal Indemnizacion) Decidir(Poliza poliza,
        EntradaDeDictamen entrada, IReadOnlyList<CodigoExclusion> exclusiones)
    {
        if (MotivoNoElegible is { } noElegible)
        {
            var elegibilidad = new Elegibilidad(noElegible, null);
            return (noElegible switch
            {
                MotivoDeNoElegibilidad.FueraDeVigencia => MotivoDeDictamen.FueraDeVigencia,
                MotivoDeNoElegibilidad.PolizaCancelada => MotivoDeDictamen.PolizaCancelada,
                MotivoDeNoElegibilidad.PrimaVencida => MotivoDeDictamen.PrimaVencida,
                _ => MotivoDeDictamen.CoberturaNoContratada,
            }, elegibilidad.Clausula, 0m);
        }

        if (entrada.Investigacion == ResultadoDeInvestigacion.FraudeComprobado)
        {
            return (MotivoDeDictamen.Fraude, Clausulas.Fraude, 0m);
        }

        if (exclusiones.Count > 0)
        {
            return (MotivoDeDictamen.Exclusion, Catalogo.De(exclusiones[0]).Clausula, 0m);
        }

        var indemnizacion = CalculoDeIndemnizacion.Calcular(entrada.PerdidaAjustada, poliza.Cobertura(Cobertura!.Value)!);
        return indemnizacion == 0
            ? (MotivoDeDictamen.PerdidaMenorAlDeducible, Clausulas.Deducible, 0m)
            : (MotivoDeDictamen.Procedente, Catalogo.De(Tipo!.Value).Clausula, indemnizacion);
    }

    private decimal Liberar(string motivo, DateTimeOffset ahora)
    {
        var liberada = Reserva;
        if (liberada != 0)
        {
            _movimientos.Add(new MovimientoDeReserva(ahora, TipoDeMovimiento.Liberacion, -liberada, 0m, motivo));
        }

        return liberada;
    }
}

namespace Perito.Domain.Producto;

/// <summary>Ramos de la aseguradora.</summary>
public enum Ramo
{
    /// <summary>Auto Integral AFV.</summary>
    Auto,

    /// <summary>Hogar Protegido AFV.</summary>
    Hogar,
}

/// <summary>Coberturas que puede tener una carátula.</summary>
public enum CodigoCobertura
{
    /// <summary>AUT-2.1.</summary>
    DanosMateriales,

    /// <summary>AUT-2.2.</summary>
    RoboTotal,

    /// <summary>AUT-2.3.</summary>
    ResponsabilidadCivilAuto,

    /// <summary>AUT-2.4.</summary>
    GastosMedicosOcupantes,

    /// <summary>AUT-2.5.</summary>
    Cristales,

    /// <summary>HOG-2.1.</summary>
    Incendio,

    /// <summary>HOG-2.2.</summary>
    DanosPorAgua,

    /// <summary>HOG-2.3.</summary>
    RoboDeContenidos,

    /// <summary>HOG-2.4.</summary>
    FenomenosHidrometeorologicos,

    /// <summary>HOG-2.5.</summary>
    ResponsabilidadCivilFamiliar,
}

/// <summary>Tipos de siniestro: cada uno lo ampara un inciso de una cobertura.</summary>
public enum TipoDeSiniestro
{
    /// <summary>AUT-2.1-a.</summary>
    Colision,

    /// <summary>AUT-2.1-b.</summary>
    Vuelco,

    /// <summary>AUT-2.1-c.</summary>
    FenomenoNaturalAuto,

    /// <summary>AUT-2.1-d.</summary>
    IncendioDeVehiculo,

    /// <summary>AUT-2.2.</summary>
    RoboTotal,

    /// <summary>AUT-2.3.</summary>
    DanosATercerosAuto,

    /// <summary>AUT-2.4.</summary>
    LesionesAOcupantes,

    /// <summary>AUT-2.5.</summary>
    RoturaDeCristales,

    /// <summary>HOG-2.1-a.</summary>
    IncendioDeVivienda,

    /// <summary>HOG-2.1-b.</summary>
    Explosion,

    /// <summary>HOG-2.2.</summary>
    DanosPorAgua,

    /// <summary>HOG-2.3.</summary>
    RoboEnVivienda,

    /// <summary>HOG-2.4-a.</summary>
    InundacionPorLluvia,

    /// <summary>HOG-2.4-b.</summary>
    GranizoOViento,

    /// <summary>HOG-2.5.</summary>
    DanosATercerosHogar,
}

/// <summary>Exclusiones de las condiciones generales.</summary>
public enum CodigoExclusion
{
    /// <summary>AUT-4.1.</summary>
    ConductorBajoInfluencia,

    /// <summary>AUT-4.2.</summary>
    UsoDistintoAlDeclarado,

    /// <summary>AUT-4.3.</summary>
    DesgasteOFallaMecanica,

    /// <summary>AUT-4.4.</summary>
    Competencias,

    /// <summary>AUT-4.5.</summary>
    ConductorSinLicencia,

    /// <summary>HOG-4.1.</summary>
    DesgasteOMantenimiento,

    /// <summary>HOG-4.2.</summary>
    RoboSinViolencia,

    /// <summary>HOG-4.3.</summary>
    ViviendaDeshabitada,

    /// <summary>HOG-4.4.</summary>
    DanoIntencional,

    /// <summary>HOG-4.5.</summary>
    ActividadComercialNoDeclarada,
}

/// <summary>Datos y documentos que el aviso debe traer según el tipo (GEN-8 y cada cobertura).</summary>
public enum DatoRequerido
{
    /// <summary>Lugar identificable del siniestro.</summary>
    Lugar,

    /// <summary>Fotografías de los daños.</summary>
    Fotografias,

    /// <summary>Presupuesto de reparación o reposición.</summary>
    PresupuestoDeReparacion,

    /// <summary>Dictamen o reporte de bomberos o de protección civil.</summary>
    DictamenDeBomberos,

    /// <summary>Denuncia ante el Ministerio Público o carpeta de investigación.</summary>
    DenunciaAnteMinisterioPublico,

    /// <summary>Informe médico de las lesiones.</summary>
    InformeMedico,

    /// <summary>Inventario de los bienes dañados o robados.</summary>
    InventarioDeBienes,

    /// <summary>Datos del tercero afectado.</summary>
    DatosDelTercero,
}

/// <summary>Lo que un tipo de siniestro significa en las condiciones: su ramo, la cobertura que lo ampara, el inciso que
/// se cita y los datos que pide el aviso.</summary>
public sealed record DefinicionDeTipo(
    TipoDeSiniestro Tipo, Ramo Ramo, CodigoCobertura Cobertura, string Clausula, IReadOnlyList<DatoRequerido> Datos);

/// <summary>Una exclusión: su ramo, su cláusula y las coberturas a las que aplica.</summary>
public sealed record DefinicionDeExclusion(
    CodigoExclusion Exclusion, Ramo Ramo, string Clausula, IReadOnlyList<CodigoCobertura> Aplica);

/// <summary>
/// El catálogo del producto tal como lo dicen las condiciones generales (docs/condiciones). Una prueba lee el
/// documento y exige que sus metadatos y este catálogo coincidan, para que el código y el texto que cita el asistente
/// no se separen.
/// </summary>
public static class Catalogo
{
    private static readonly DatoRequerido L = DatoRequerido.Lugar;
    private static readonly DatoRequerido F = DatoRequerido.Fotografias;
    private static readonly DatoRequerido P = DatoRequerido.PresupuestoDeReparacion;
    private static readonly DatoRequerido B = DatoRequerido.DictamenDeBomberos;
    private static readonly DatoRequerido D = DatoRequerido.DenunciaAnteMinisterioPublico;
    private static readonly DatoRequerido M = DatoRequerido.InformeMedico;
    private static readonly DatoRequerido I = DatoRequerido.InventarioDeBienes;
    private static readonly DatoRequerido X = DatoRequerido.DatosDelTercero;

    private static readonly Dictionary<TipoDeSiniestro, DefinicionDeTipo> PorTipo = new[]
    {
        Tipo(TipoDeSiniestro.Colision, Ramo.Auto, CodigoCobertura.DanosMateriales, "AUT-2.1-a", L, F, P),
        Tipo(TipoDeSiniestro.Vuelco, Ramo.Auto, CodigoCobertura.DanosMateriales, "AUT-2.1-b", L, F, P),
        Tipo(TipoDeSiniestro.FenomenoNaturalAuto, Ramo.Auto, CodigoCobertura.DanosMateriales, "AUT-2.1-c", L, F),
        Tipo(TipoDeSiniestro.IncendioDeVehiculo, Ramo.Auto, CodigoCobertura.DanosMateriales, "AUT-2.1-d", L, B),
        Tipo(TipoDeSiniestro.RoboTotal, Ramo.Auto, CodigoCobertura.RoboTotal, "AUT-2.2", L, D),
        Tipo(TipoDeSiniestro.DanosATercerosAuto, Ramo.Auto, CodigoCobertura.ResponsabilidadCivilAuto, "AUT-2.3", L, X),
        Tipo(TipoDeSiniestro.LesionesAOcupantes, Ramo.Auto, CodigoCobertura.GastosMedicosOcupantes, "AUT-2.4", L, M),
        Tipo(TipoDeSiniestro.RoturaDeCristales, Ramo.Auto, CodigoCobertura.Cristales, "AUT-2.5", F, P),
        Tipo(TipoDeSiniestro.IncendioDeVivienda, Ramo.Hogar, CodigoCobertura.Incendio, "HOG-2.1-a", B, I),
        Tipo(TipoDeSiniestro.Explosion, Ramo.Hogar, CodigoCobertura.Incendio, "HOG-2.1-b", B, I),
        Tipo(TipoDeSiniestro.DanosPorAgua, Ramo.Hogar, CodigoCobertura.DanosPorAgua, "HOG-2.2", F, P),
        Tipo(TipoDeSiniestro.RoboEnVivienda, Ramo.Hogar, CodigoCobertura.RoboDeContenidos, "HOG-2.3", D, I),
        Tipo(TipoDeSiniestro.InundacionPorLluvia, Ramo.Hogar, CodigoCobertura.FenomenosHidrometeorologicos,
            "HOG-2.4-a", F, I),
        Tipo(TipoDeSiniestro.GranizoOViento, Ramo.Hogar, CodigoCobertura.FenomenosHidrometeorologicos, "HOG-2.4-b",
            F, P),
        Tipo(TipoDeSiniestro.DanosATercerosHogar, Ramo.Hogar, CodigoCobertura.ResponsabilidadCivilFamiliar, "HOG-2.5",
            X, P),
    }.ToDictionary(d => d.Tipo);

    private static readonly Dictionary<CodigoExclusion, DefinicionDeExclusion> PorExclusion = new[]
    {
        Exclusion(CodigoExclusion.ConductorBajoInfluencia, Ramo.Auto, "AUT-4.1", CodigoCobertura.DanosMateriales,
            CodigoCobertura.GastosMedicosOcupantes, CodigoCobertura.Cristales),
        Exclusion(CodigoExclusion.UsoDistintoAlDeclarado, Ramo.Auto, "AUT-4.2", CodigoCobertura.DanosMateriales,
            CodigoCobertura.RoboTotal, CodigoCobertura.ResponsabilidadCivilAuto, CodigoCobertura.GastosMedicosOcupantes,
            CodigoCobertura.Cristales),
        Exclusion(CodigoExclusion.DesgasteOFallaMecanica, Ramo.Auto, "AUT-4.3", CodigoCobertura.DanosMateriales),
        Exclusion(CodigoExclusion.Competencias, Ramo.Auto, "AUT-4.4", CodigoCobertura.DanosMateriales,
            CodigoCobertura.ResponsabilidadCivilAuto, CodigoCobertura.GastosMedicosOcupantes),
        Exclusion(CodigoExclusion.ConductorSinLicencia, Ramo.Auto, "AUT-4.5", CodigoCobertura.DanosMateriales),
        Exclusion(CodigoExclusion.DesgasteOMantenimiento, Ramo.Hogar, "HOG-4.1", CodigoCobertura.DanosPorAgua,
            CodigoCobertura.FenomenosHidrometeorologicos, CodigoCobertura.Incendio),
        Exclusion(CodigoExclusion.RoboSinViolencia, Ramo.Hogar, "HOG-4.2", CodigoCobertura.RoboDeContenidos),
        Exclusion(CodigoExclusion.ViviendaDeshabitada, Ramo.Hogar, "HOG-4.3", CodigoCobertura.RoboDeContenidos,
            CodigoCobertura.DanosPorAgua),
        Exclusion(CodigoExclusion.DanoIntencional, Ramo.Hogar, "HOG-4.4", CodigoCobertura.Incendio,
            CodigoCobertura.DanosPorAgua, CodigoCobertura.RoboDeContenidos,
            CodigoCobertura.FenomenosHidrometeorologicos, CodigoCobertura.ResponsabilidadCivilFamiliar),
        Exclusion(CodigoExclusion.ActividadComercialNoDeclarada, Ramo.Hogar, "HOG-4.5", CodigoCobertura.Incendio,
            CodigoCobertura.ResponsabilidadCivilFamiliar),
    }.ToDictionary(d => d.Exclusion);

    /// <summary>Todos los tipos de siniestro.</summary>
    public static IReadOnlyCollection<DefinicionDeTipo> Tipos => PorTipo.Values;

    /// <summary>Todas las exclusiones.</summary>
    public static IReadOnlyCollection<DefinicionDeExclusion> Exclusiones => PorExclusion.Values;

    /// <summary>La definición de un tipo.</summary>
    public static DefinicionDeTipo De(TipoDeSiniestro tipo) => PorTipo[tipo];

    /// <summary>La definición de una exclusión.</summary>
    public static DefinicionDeExclusion De(CodigoExclusion exclusion) => PorExclusion[exclusion];

    /// <summary>El ramo al que pertenece una cobertura.</summary>
    public static Ramo RamoDe(CodigoCobertura cobertura) => cobertura switch
    {
        CodigoCobertura.DanosMateriales or CodigoCobertura.RoboTotal or CodigoCobertura.ResponsabilidadCivilAuto
            or CodigoCobertura.GastosMedicosOcupantes or CodigoCobertura.Cristales => Ramo.Auto,
        _ => Ramo.Hogar,
    };

    /// <summary>Busca un tipo por el inciso que lo ampara (lo que cita el asistente).</summary>
    public static DefinicionDeTipo? TipoPorClausula(string clausula) =>
        PorTipo.Values.FirstOrDefault(d => string.Equals(d.Clausula, clausula, StringComparison.Ordinal));

    /// <summary>Busca una exclusión por su cláusula.</summary>
    public static DefinicionDeExclusion? ExclusionPorClausula(string clausula) =>
        PorExclusion.Values.FirstOrDefault(d => string.Equals(d.Clausula, clausula, StringComparison.Ordinal));

    private static DefinicionDeTipo Tipo(TipoDeSiniestro tipo, Ramo ramo, CodigoCobertura cobertura, string clausula,
        params DatoRequerido[] datos) => new(tipo, ramo, cobertura, clausula, datos);

    private static DefinicionDeExclusion Exclusion(CodigoExclusion exclusion, Ramo ramo, string clausula,
        params CodigoCobertura[] aplica) => new(exclusion, ramo, clausula, aplica);
}

/// <summary>Cláusulas generales que funda una decisión del dominio (GEN-x).</summary>
public static class Clausulas
{
    /// <summary>Definiciones: cobertura contratada.</summary>
    public const string Definiciones = "GEN-1";

    /// <summary>Vigencia y cancelación.</summary>
    public const string Vigencia = "GEN-2";

    /// <summary>Prima y periodo de gracia.</summary>
    public const string Prima = "GEN-3";

    /// <summary>Aviso de siniestro.</summary>
    public const string Aviso = "GEN-4";

    /// <summary>Suma asegurada.</summary>
    public const string SumaAsegurada = "GEN-5";

    /// <summary>Deducible.</summary>
    public const string Deducible = "GEN-6";

    /// <summary>Coaseguro y cálculo.</summary>
    public const string Calculo = "GEN-7";

    /// <summary>Documentación.</summary>
    public const string Documentacion = "GEN-8";

    /// <summary>Indicios de fraude.</summary>
    public const string Fraude = "GEN-9";

    /// <summary>Plazo de pago.</summary>
    public const string Pago = "GEN-10";

    /// <summary>Rechazo por escrito.</summary>
    public const string Rechazo = "GEN-11";
}

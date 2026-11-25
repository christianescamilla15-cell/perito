using Perito.Domain.Producto;
using Perito.Domain.Comun;

namespace Perito.Domain.Polizas;

/// <summary>Cómo se calcula el deducible según la carátula (GEN-6).</summary>
public enum TipoDeDeducible
{
    /// <summary>Una cantidad fija en pesos.</summary>
    Fijo,

    /// <summary>Un porcentaje (fracción) de la suma asegurada.</summary>
    PorcentajeSuma,

    /// <summary>Un porcentaje (fracción) de la pérdida.</summary>
    PorcentajePerdida,
}

/// <summary>Deducible de una cobertura.</summary>
public sealed record Deducible(TipoDeDeducible Tipo, decimal Valor)
{
    /// <summary>El monto del deducible para una pérdida y una suma asegurada, al centavo.</summary>
    public decimal Monto(decimal perdida, decimal sumaAsegurada) => Pesos.Redondear(Tipo switch
    {
        TipoDeDeducible.Fijo => Valor,
        TipoDeDeducible.PorcentajeSuma => sumaAsegurada * Valor,
        _ => perdida * Valor,
    });

    /// <summary>Un deducible fijo no es negativo; un porcentaje va de 0 a 1.</summary>
    public bool EsValido => Valor >= 0 && (Tipo == TipoDeDeducible.Fijo || Valor <= 1);
}

/// <summary>Una cobertura de la carátula con su suma asegurada, deducible y coaseguro.</summary>
public sealed record CoberturaContratada(
    CodigoCobertura Codigo, decimal SumaAsegurada, Deducible Deducible, decimal Coaseguro);

/// <summary>Por qué un siniestro no es elegible (antes de ver la pérdida).</summary>
public enum MotivoDeNoElegibilidad
{
    /// <summary>Ocurrió fuera de la vigencia (GEN-2).</summary>
    FueraDeVigencia,

    /// <summary>Ocurrió desde la fecha de cancelación (GEN-2).</summary>
    PolizaCancelada,

    /// <summary>Ocurrió con la prima vencida después del periodo de gracia (GEN-3).</summary>
    PrimaVencida,

    /// <summary>La cobertura que lo ampararía no está en la carátula (GEN-1).</summary>
    CoberturaNoContratada,
}

/// <summary>Elegibilidad de un siniestro: si la póliza lo ampara por fechas y por cobertura.</summary>
public sealed record Elegibilidad(MotivoDeNoElegibilidad? Motivo, CoberturaContratada? Cobertura)
{
    /// <summary>Verdadero si no hay motivo de no elegibilidad.</summary>
    public bool EsElegible => Motivo is null;

    /// <summary>La cláusula que funda el motivo.</summary>
    public string? Clausula => Motivo switch
    {
        MotivoDeNoElegibilidad.FueraDeVigencia or MotivoDeNoElegibilidad.PolizaCancelada => Clausulas.Vigencia,
        MotivoDeNoElegibilidad.PrimaVencida => Clausulas.Prima,
        MotivoDeNoElegibilidad.CoberturaNoContratada => Clausulas.Definiciones,
        _ => null,
    };
}

/// <summary>
/// Póliza: el contrato con su vigencia, el pago de la prima, la cancelación y la carátula. No guarda datos personales:
/// sólo el identificador del asegurado.
/// </summary>
public sealed class Poliza : RaizDeAgregado
{
    /// <summary>Días naturales del periodo de gracia (GEN-3).</summary>
    public const int DiasDeGracia = 30;

    private readonly List<CoberturaContratada> _coberturas;

    private Poliza(Guid id, string folio, Ramo ramo, Guid aseguradoId, DateOnly inicio, DateOnly fin,
        DateOnly primaPagadaHasta, DateOnly? cancelacion, IEnumerable<CoberturaContratada> coberturas)
    {
        Id = id;
        Folio = folio;
        Ramo = ramo;
        AseguradoId = aseguradoId;
        InicioVigencia = inicio;
        FinVigencia = fin;
        PrimaPagadaHasta = primaPagadaHasta;
        FechaCancelacion = cancelacion;
        _coberturas = coberturas.ToList();
    }

    /// <summary>Folio de la póliza (p. ej. AFV-AUT-000001).</summary>
    public string Folio { get; }

    /// <summary>Ramo del producto.</summary>
    public Ramo Ramo { get; }

    /// <summary>Titular (sólo su identificador).</summary>
    public Guid AseguradoId { get; }

    /// <summary>Primer día de vigencia, inclusive.</summary>
    public DateOnly InicioVigencia { get; }

    /// <summary>Último día de vigencia, inclusive.</summary>
    public DateOnly FinVigencia { get; }

    /// <summary>Último día cubierto por la prima pagada.</summary>
    public DateOnly PrimaPagadaHasta { get; private set; }

    /// <summary>Fecha desde la que la póliza está cancelada, si lo está.</summary>
    public DateOnly? FechaCancelacion { get; private set; }

    /// <summary>La carátula.</summary>
    public IReadOnlyList<CoberturaContratada> Coberturas => _coberturas;

    /// <summary>Emite una póliza validando su carátula.</summary>
    public static Resultado<Poliza> Emitir(Guid id, string folio, Ramo ramo, Guid aseguradoId, DateOnly inicio,
        DateOnly fin, DateOnly primaPagadaHasta, IReadOnlyCollection<CoberturaContratada> coberturas)
    {
        if (string.IsNullOrWhiteSpace(folio) || folio.Length > 40)
        {
            return Resultado<Poliza>.Fallo("folio_invalido", "El folio es obligatorio y tiene hasta 40 caracteres.");
        }

        if (fin < inicio)
        {
            return Resultado<Poliza>.Fallo("vigencia_invalida", "El fin de vigencia es anterior al inicio.");
        }

        if (primaPagadaHasta < inicio)
        {
            return Resultado<Poliza>.Fallo("prima_invalida", "La prima pagada no puede terminar antes del inicio.");
        }

        if (coberturas.Count == 0)
        {
            return Resultado<Poliza>.Fallo("caratula_vacia", "La carátula necesita al menos una cobertura.");
        }

        if (coberturas.Any(c => Catalogo.RamoDe(c.Codigo) != ramo))
        {
            return Resultado<Poliza>.Fallo("cobertura_de_otro_ramo", $"Todas las coberturas deben ser de {ramo}.");
        }

        if (coberturas.GroupBy(c => c.Codigo).Any(g => g.Count() > 1))
        {
            return Resultado<Poliza>.Fallo("cobertura_repetida", "Una cobertura aparece dos veces en la carátula.");
        }

        if (coberturas.Any(c => c.SumaAsegurada <= 0 || !c.Deducible.EsValido || c.Coaseguro is < 0 or > 0.5m))
        {
            return Resultado<Poliza>.Fallo("cobertura_invalida",
                "Suma asegurada positiva, deducible válido y coaseguro entre 0 y 0.5.");
        }

        return Resultado<Poliza>.Exito(new Poliza(id, folio.Trim(), ramo, aseguradoId, inicio, fin, primaPagadaHasta,
            null, coberturas));
    }

    /// <summary>Reconstruye una póliza guardada (sin validar de nuevo).</summary>
    public static Poliza Rehidratar(Guid id, string folio, Ramo ramo, Guid aseguradoId, DateOnly inicio, DateOnly fin,
        DateOnly primaPagadaHasta, DateOnly? cancelacion, IEnumerable<CoberturaContratada> coberturas) =>
        new(id, folio, ramo, aseguradoId, inicio, fin, primaPagadaHasta, cancelacion, coberturas);

    /// <summary>Cancela la póliza desde una fecha.</summary>
    public Resultado Cancelar(DateOnly desde)
    {
        if (FechaCancelacion is not null)
        {
            return Resultado.Fallo("ya_cancelada", "La póliza ya estaba cancelada.");
        }

        FechaCancelacion = desde;
        return Resultado.Exito();
    }

    /// <summary>La cobertura de la carátula, si está contratada.</summary>
    public CoberturaContratada? Cobertura(CodigoCobertura codigo) => _coberturas.FirstOrDefault(c => c.Codigo == codigo);

    /// <summary>Vigencia, cancelación y prima en la fecha de ocurrencia (GEN-2 y GEN-3), sin mirar la cobertura.</summary>
    public MotivoDeNoElegibilidad? AmparaLaFecha(DateOnly ocurrencia)
    {
        if (ocurrencia < InicioVigencia || ocurrencia > FinVigencia)
        {
            return MotivoDeNoElegibilidad.FueraDeVigencia;
        }

        if (FechaCancelacion is { } cancelacion && ocurrencia >= cancelacion)
        {
            return MotivoDeNoElegibilidad.PolizaCancelada;
        }

        // El periodo de gracia cuenta desde el día siguiente a PrimaPagadaHasta: el día 30 todavía ampara.
        return ocurrencia > PrimaPagadaHasta.AddDays(DiasDeGracia) ? MotivoDeNoElegibilidad.PrimaVencida : null;
    }

    /// <summary>Elegibilidad completa: fechas y cobertura contratada.</summary>
    public Elegibilidad Elegibilidad(DateOnly ocurrencia, CodigoCobertura cobertura)
    {
        var contratada = Cobertura(cobertura);
        var motivo = AmparaLaFecha(ocurrencia)
            ?? (contratada is null ? MotivoDeNoElegibilidad.CoberturaNoContratada : null);
        return new Elegibilidad(motivo, contratada);
    }
}

/// <summary>Cálculo de la indemnización (GEN-5, GEN-6 y GEN-7).</summary>
public static class CalculoDeIndemnizacion
{
    /// <summary>
    /// (pérdida − deducible) × (1 − coaseguro), sin exceder la suma asegurada, al centavo. Una pérdida igual o menor que
    /// el deducible da cero. Los ejemplos de GEN-7 son el oráculo de sus pruebas.
    /// </summary>
    public static decimal Calcular(decimal perdida, CoberturaContratada cobertura)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(perdida);
        var deducible = cobertura.Deducible.Monto(perdida, cobertura.SumaAsegurada);
        var neto = Math.Max(0m, perdida - deducible);
        var aCargoDeLaAseguradora = neto * (1 - cobertura.Coaseguro);
        return Pesos.Redondear(Math.Min(cobertura.SumaAsegurada, aCargoDeLaAseguradora));
    }
}

using Perito.Domain.Ajustadores;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;

namespace Perito.Domain.Tests;

/// <summary>Pólizas y siniestros de prueba con las carátulas de eval/polizas.json (auto amplia y hogar completa).</summary>
internal static class Escenario
{
    public static readonly DateTimeOffset Ahora = new(2026, 6, 15, 17, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Hoy = new(2026, 6, 15);
    public static readonly DateOnly Inicio = new(2026, 1, 1);
    public static readonly DateOnly Fin = new(2026, 12, 31);

    public static CoberturaContratada Cobertura(CodigoCobertura codigo, decimal suma, TipoDeDeducible tipo,
        decimal deducible, decimal coaseguro = 0) => new(codigo, suma, new Deducible(tipo, deducible), coaseguro);

    public static Poliza AutoAmplia(DateOnly? inicio = null, DateOnly? primaHasta = null) => Poliza.Emitir(
        Guid.NewGuid(), "AFV-AUT-000001", Ramo.Auto, Guid.NewGuid(), inicio ?? Inicio, Fin, primaHasta ?? Fin,
        [
            Cobertura(CodigoCobertura.DanosMateriales, 350_000m, TipoDeDeducible.PorcentajeSuma, 0.05m),
            Cobertura(CodigoCobertura.RoboTotal, 350_000m, TipoDeDeducible.PorcentajeSuma, 0.10m),
            Cobertura(CodigoCobertura.ResponsabilidadCivilAuto, 1_500_000m, TipoDeDeducible.Fijo, 0m),
            Cobertura(CodigoCobertura.GastosMedicosOcupantes, 200_000m, TipoDeDeducible.Fijo, 0m),
            Cobertura(CodigoCobertura.Cristales, 30_000m, TipoDeDeducible.PorcentajePerdida, 0.20m),
        ]).Valor;

    public static Poliza AutoLimitada() => Poliza.Emitir(Guid.NewGuid(), "AFV-AUT-000002", Ramo.Auto, Guid.NewGuid(),
        Inicio, Fin, Fin,
        [
            Cobertura(CodigoCobertura.RoboTotal, 220_000m, TipoDeDeducible.PorcentajeSuma, 0.10m),
            Cobertura(CodigoCobertura.ResponsabilidadCivilAuto, 1_000_000m, TipoDeDeducible.Fijo, 0m),
        ]).Valor;

    public static Poliza HogarCompleta() => Poliza.Emitir(Guid.NewGuid(), "AFV-HOG-000001", Ramo.Hogar,
        Guid.NewGuid(), Inicio, Fin, Fin,
        [
            Cobertura(CodigoCobertura.Incendio, 2_500_000m, TipoDeDeducible.Fijo, 5_000m),
            Cobertura(CodigoCobertura.DanosPorAgua, 150_000m, TipoDeDeducible.Fijo, 3_000m),
            Cobertura(CodigoCobertura.RoboDeContenidos, 250_000m, TipoDeDeducible.PorcentajePerdida, 0.10m),
            Cobertura(CodigoCobertura.FenomenosHidrometeorologicos, 2_500_000m, TipoDeDeducible.PorcentajeSuma,
                0.02m, 0.10m),
            Cobertura(CodigoCobertura.ResponsabilidadCivilFamiliar, 1_000_000m, TipoDeDeducible.Fijo, 0m),
        ]).Valor;

    public static Ajustador AjustadorDe(params Especialidad[] especialidades) =>
        Ajustador.Registrar(Guid.NewGuid(), "Ajustador de prueba", especialidades).Valor;

    public static Siniestro Avisado(Poliza poliza, DateOnly? ocurrencia = null, DateOnly? aviso = null,
        decimal? estimacion = null) =>
        Siniestro.Avisar(Guid.NewGuid(), poliza, ocurrencia ?? Hoy.AddDays(-1), aviso ?? Hoy,
            "Choque en la avenida del Lago contra un poste; el cofre quedó doblado.", estimacion, null, Ahora).Valor;

    public static Siniestro Triado(Poliza poliza, TipoDeSiniestro tipo = TipoDeSiniestro.Colision,
        DateOnly? ocurrencia = null, int previos = 0)
    {
        var s = Avisado(poliza, ocurrencia);
        Exito(s.Triar(tipo, poliza, previos, "ajustador.uno", null, Ahora));
        return s;
    }

    public static Siniestro Asignado(Poliza poliza, TipoDeSiniestro tipo = TipoDeSiniestro.Colision,
        DateOnly? ocurrencia = null)
    {
        var s = Triado(poliza, tipo, ocurrencia);
        var especialidad = poliza.Ramo == Ramo.Auto ? Especialidad.Auto : Especialidad.Hogar;
        Exito(s.AsignarAjustador(AjustadorDe(especialidad), 0, Ahora));
        return s;
    }

    public static Siniestro Reservado(Poliza poliza, TipoDeSiniestro tipo = TipoDeSiniestro.Colision,
        decimal estimacion = 100_000m, bool conDocumentos = true)
    {
        var s = Asignado(poliza, tipo);
        Exito(s.ConstituirReserva(poliza, estimacion, Ahora));
        if (conDocumentos)
        {
            foreach (var dato in Catalogo.De(tipo).Datos)
            {
                Exito(s.RegistrarDatoRecibido(dato));
            }
        }

        return s;
    }

    public static Siniestro Dictaminado(Poliza poliza, decimal perdida = 100_000m,
        params CodigoExclusion[] exclusiones)
    {
        var s = Reservado(poliza);
        Exito(s.Dictaminar(poliza, new EntradaDeDictamen(perdida, exclusiones, null), "ajustador.uno", Hoy, Ahora));
        return s;
    }

    public static void Exito(Resultado r) => Assert.True(r.EsExito, r.Error?.Codigo + ": " + r.Error?.Mensaje);

    public static void Falla(Resultado r, string codigo)
    {
        Assert.False(r.EsExito, $"se esperaba {codigo} y la operación se aplicó");
        Assert.Equal(codigo, r.Error!.Codigo);
    }
}

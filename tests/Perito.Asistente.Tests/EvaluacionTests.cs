using System.Text.Json;
using Perito.Asistente;
using Perito.Evaluacion;

namespace Perito.Asistente.Tests;

/// <summary>Una sola corrida del conjunto completo (tres contextos) para todas las pruebas de la clase.</summary>
public sealed class CorridaDelConjunto : IAsyncLifetime
{
    public static readonly string Eval = Conjunto.Buscar(AppContext.BaseDirectory);

    public Informe Informe { get; private set; } = null!;

    public async Task InitializeAsync() =>
        Informe = await new Evaluador(Eval, new OpcionesDelAsistente { Proveedor = "extractivo" }).CorrerAsync();

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// La compuerta del CI con el modelo extractivo y el control negativo: con el contexto vacío o el de otro caso, la
/// cita y la clasificación caen; ningún prompt de los 50 casos lleva un dato personal conocido.
/// </summary>
public class EvaluacionTests(CorridaDelConjunto corrida) : IClassFixture<CorridaDelConjunto>
{
    private Metricas De(Contexto c) => corrida.Informe.Corridas.Single(x => x.Contexto == c).Metricas;

    [Fact]
    public void La_compuerta_de_umbrales_pasa_con_el_modelo_extractivo() =>
        Assert.Empty(Compuerta.Revisar(corrida.Informe, Path.Join(CorridaDelConjunto.Eval, "umbrales.json")));

    [Fact]
    public void Con_el_contexto_vacio_o_barajado_la_cita_y_la_clasificacion_caen()
    {
        var normal = De(Contexto.Normal);
        foreach (var control in new[] { De(Contexto.Vacio), De(Contexto.Barajado) })
        {
            Assert.True(normal.Cita - control.Cita >= 0.5, $"cita {normal.Cita} → {control.Cita}");
            Assert.True(normal.Clasificacion - control.Clasificacion >= 0.5,
                $"clasificación {normal.Clasificacion} → {control.Clasificacion}");
            Assert.True(control.DatosF1 < normal.DatosF1);
        }
    }

    [Fact]
    public void Ningun_prompt_de_ningun_caso_lleva_un_dato_personal_conocido() =>
        Assert.All(corrida.Informe.Corridas.SelectMany(c => c.Casos), r => Assert.Equal(0, r.PiiEnPrompt));

    [Fact]
    public void El_informe_versionado_es_el_que_produce_esta_version()
    {
        var versionado = File.ReadAllText(Path.Join(CorridaDelConjunto.Eval, "informe", "extractivo.json"));
        var actual = JsonSerializer.Serialize(corrida.Informe, Program.Json) + "\n";
        Assert.True(versionado.ReplaceLineEndings("\n") == actual,
            "eval/informe/extractivo.json no corresponde a esta versión: regenérelo con " +
            "`dotnet run --project src/Perito.Evaluacion` y revise el cambio de cifras.");
    }

    [Fact]
    public void El_control_barajado_usa_otro_caso_del_mismo_ramo_con_otra_clausula()
    {
        var casos = Conjunto.Casos(CorridaDelConjunto.Eval);
        var caratulas = Conjunto.Caratulas(CorridaDelConjunto.Eval);
        for (var i = 0; i < casos.Count; i++)
        {
            var pareja = Evaluador.Pareja(casos, caratulas, i);
            Assert.NotEqual(casos[i].Id, pareja.Id);
            Assert.Equal(caratulas[casos[i].Poliza].Ramo, caratulas[pareja.Poliza].Ramo);
            Assert.NotEqual(casos[i].Esperado.Clausula, pareja.Esperado.Clausula);
        }
    }

    [Fact]
    public void El_conjunto_tiene_cincuenta_casos_con_etiquetas_del_catalogo()
    {
        var casos = Conjunto.Casos(CorridaDelConjunto.Eval);
        Assert.Equal(50, casos.Count);
        Assert.Equal(50, casos.Select(c => c.Id).Distinct().Count());
        Assert.All(casos, c =>
        {
            var tipo = Domain.Producto.Catalogo.De(Enum.Parse<Domain.Producto.TipoDeSiniestro>(c.Esperado.Tipo));
            Assert.Equal(tipo.Clausula, c.Esperado.Clausula);
            Assert.Equal(tipo.Cobertura.ToString(), c.Esperado.Cobertura);
            Assert.All(c.Esperado.DatosFaltantes, d => Assert.Contains(d, tipo.Datos.Select(x => x.ToString())));
            Assert.All(c.Pii, v => Assert.Contains(v, c.Descripcion, StringComparison.Ordinal));
        });
    }

    /// <summary>Oráculo a mano: esperado {A,B},{C}; propuesto {A},{C,D} → 2 aciertos, 1 de más, 1 de menos.</summary>
    [Fact]
    public void Las_metricas_micro_cuentan_aciertos_sobrantes_y_faltantes()
    {
        var (p, r, f1) = Evaluador.Micro([(["A", "B"], ["A"]), (["C"], ["C", "D"])]);
        Assert.Equal((0.6667, 0.6667, 0.6667), (p, r, f1));
        Assert.Equal((1.0, 0.0, 0.0), Evaluador.Micro([(["A"], [])]));
    }

    [Fact]
    public void La_compuerta_falla_si_el_control_negativo_no_hace_caer_la_cita()
    {
        var normal = corrida.Informe.Corridas.Single(c => c.Contexto == Contexto.Normal);
        var trucado = corrida.Informe with
        {
            Corridas = corrida.Informe.Corridas
                .Select(c => c.Contexto == Contexto.Barajado ? c with { Metricas = normal.Metricas } : c).ToList(),
        };
        var fallas = Compuerta.Revisar(trucado, Path.Join(CorridaDelConjunto.Eval, "umbrales.json"));
        Assert.Contains(fallas, f => f.Contains("barajado", StringComparison.Ordinal));
    }
}

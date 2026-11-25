using Perito.Application.Puertos;
using Perito.Asistente;
using Perito.Asistente.Corpus;
using Perito.Asistente.Recuperacion;
using Perito.Domain.Privacidad;
using Perito.Domain.Producto;

namespace Perito.Asistente.Tests;

/// <summary>Corpus, recuperación BM25 y formato del prompt.</summary>
public class RecuperacionYPromptTests
{
    [Fact]
    public void El_corpus_incrustado_trae_todas_las_clausulas_con_sus_metadatos_y_sin_el_comentario()
    {
        var f = LectorDeCondiciones.Fragmentos;
        Assert.Equal(Catalogo.Tipos.Count, f.Count(x => x.EsCobertura));
        Assert.Equal(Catalogo.Exclusiones.Count, f.Count(x => x.EsExclusion));
        Assert.Equal(12, f.Count(x => x.Ramo == "General"));
        var colision = Assert.Single(f, x => x.Id == "AUT-2.1-a");
        Assert.Equal(("Colision", "DanosMateriales"), (colision.Tipo, colision.Cobertura));
        Assert.Equal(["Lugar", "Fotografias", "PresupuestoDeReparacion"], colision.Datos);
        Assert.DoesNotContain("<!--", colision.Texto, StringComparison.Ordinal);
        Assert.StartsWith("Ampara los daños al vehículo asegurado por colisión", colision.Texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Choques", "choqu")]
    [InlineData("robaron", "rob")]
    [InlineData("robo", "rob")]
    [InlineData("inundación", "inund")]
    [InlineData("inundó", "inund")]
    [InlineData("granizada", "graniz")]
    public void La_raiz_ligera_junta_formas_de_la_misma_palabra(string palabra, string raiz) =>
        Assert.Equal(raiz, Texto.Raiz(Texto.Normalizar(palabra)));

    [Fact]
    public void La_recuperacion_respeta_el_ramo_y_las_cuotas()
    {
        var r = new RecuperadorBm25();
        var hogar = r.Buscar("se rompió la tubería y el agua inundó la cocina; también robaron la televisión", Ramo.Hogar);
        Assert.All(hogar.Todos, f => Assert.StartsWith("HOG-", f.Id, StringComparison.Ordinal));
        Assert.InRange(hogar.Coberturas.Count, 1, RecuperadorBm25.CoberturasARecuperar);
        Assert.InRange(hogar.Exclusiones.Count, 0, RecuperadorBm25.ExclusionesARecuperar);
        Assert.All(hogar.Coberturas, f => Assert.True(f.EsCobertura));
        Assert.All(hogar.Exclusiones, f => Assert.True(f.EsExclusion));
    }

    [Theory]
    [InlineData("Un camión me alcanzó por detrás y chocó contra la defensa", Ramo.Auto, "AUT-2.1-a")]
    [InlineData("Se llevaron mi camioneta, la robaron en la noche", Ramo.Auto, "AUT-2.2")]
    [InlineData("Estrellaron el parabrisas con una piedra", Ramo.Auto, "AUT-2.5")]
    [InlineData("Explotó el tanque de gas", Ramo.Hogar, "HOG-2.1-b")]
    public void La_clausula_que_describe_el_hecho_sale_primero(string texto, Ramo ramo, string esperada) =>
        Assert.Equal(esperada, new RecuperadorBm25().Buscar(texto, ramo).Coberturas[0].Id);

    [Fact]
    public void Un_texto_sin_palabras_del_corpus_no_recupera_nada() =>
        Assert.Empty(new RecuperadorBm25().Buscar("xyz qwerty", Ramo.Auto).Todos);

    [Fact]
    public void La_recuperacion_es_determinista()
    {
        const string texto = "Cayó granizo sobre el coche estacionado y quedó abollado";
        var a = new RecuperadorBm25().Buscar(texto, Ramo.Auto).Todos.Select(f => f.Id);
        var b = new RecuperadorBm25().Buscar(texto, Ramo.Auto).Todos.Select(f => f.Id);
        Assert.Equal(a, b);
    }

    [Fact]
    public void El_prompt_se_lee_de_vuelta_con_ids_y_metadatos()
    {
        var contexto = new RecuperadorBm25().Buscar("me robaron el coche; ya tengo la denuncia", Ramo.Auto);
        var texto = Seudonimizador.Seudonimizar("Me robaron el coche; ya tengo la denuncia.", []);
        var mensaje = Prompt.Mensaje(new SolicitudAlAsistente(Guid.NewGuid(), Ramo.Auto, texto), contexto);

        var (descripcion, clausulas) = Prompt.Leer(mensaje);
        Assert.Equal(texto.Texto, descripcion);
        Assert.Equal(contexto.Todos.Select(f => f.Id), clausulas.Select(f => f.Id));
        Assert.Equal(contexto.Todos.Select(f => (f.Tipo, f.Cobertura, f.Exclusion)),
            clausulas.Select(f => (f.Tipo, f.Cobertura, f.Exclusion)));
        Assert.Equal(contexto.Todos.Select(f => string.Join(",", f.Datos)), clausulas.Select(f => string.Join(",", f.Datos)));
    }

    [Fact]
    public void Las_instrucciones_dicen_que_propone_y_no_decide_y_nombran_la_unica_funcion()
    {
        Assert.Contains("PROPONES; no decides", Prompt.Sistema, StringComparison.Ordinal);
        Assert.Contains("poliza-coberturas_contratadas", Prompt.Sistema, StringComparison.Ordinal);
        Assert.Contains("[PERSONA_1]", Prompt.Sistema, StringComparison.Ordinal);
    }

    [Fact]
    public void Sin_contexto_el_prompt_lo_dice()
    {
        var texto = Seudonimizador.Seudonimizar("Algo pasó con el coche ayer en la noche.", []);
        var mensaje = Prompt.Mensaje(new SolicitudAlAsistente(Guid.NewGuid(), Ramo.Auto, texto), ContextoRecuperado.Vacio);
        Assert.Contains("(ninguna)", mensaje, StringComparison.Ordinal);
        Assert.Empty(Prompt.Leer(mensaje).Clausulas);
    }
}

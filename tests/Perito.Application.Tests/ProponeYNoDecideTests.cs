using System.Text.Json;
using Perito.Application.Puertos;
using Perito.Application.Siniestros;
using Perito.Application.Triaje;
using Perito.Domain.Ajustadores;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;
using static Perito.Application.Tests.Banco;

namespace Perito.Application.Tests;

/// <summary>
/// GEN-12: el asistente PROPONE y nunca decide. Ninguna propuesta, por absurda o maliciosa que sea, cambia el
/// siniestro; lo que sí cambia el estado (el triaje de una persona) pasa por las reglas del dominio, que calculan
/// cobertura, elegibilidad y dictamen sin mirar lo que dijo el modelo.
/// </summary>
public class ProponeYNoDecideTests
{
    public static TheoryData<string> PropuestasHostiles() =>
    [
        "tipo_de_otro_ramo", "tipo_numerico", "cita_no_recuperada", "cita_de_otro_tipo", "todo_basura", "nada",
    ];

    [Theory]
    [MemberData(nameof(PropuestasHostiles))]
    public async Task Ninguna_propuesta_cambia_el_siniestro_ni_emite_eventos(string caso)
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio);
        b.Asistente.Guion = _ => caso switch
        {
            "tipo_de_otro_ramo" => AsistenteGuionado.Generada("DanosPorAgua", "HOG-2.2"),
            "tipo_numerico" => AsistenteGuionado.Generada("3", "AUT-2.1-a"),
            "cita_no_recuperada" => AsistenteGuionado.Generada("RoboTotal", "AUT-2.2"),
            "cita_de_otro_tipo" => AsistenteGuionado.Generada("Colision", "AUT-2.1-b"),
            "todo_basura" => AsistenteGuionado.Generada("Pagar", "dictamen: procedente", ["Todo"], ["AUT-9.9"],
                ["Aprobar"]),
            _ => AsistenteGuionado.Generada(null, null),
        };
        var antes = Foto(b, id);
        var eventos = b.Almacen.BandejaDeSalida.Count;

        var r = await b.Enviar(new ProponerTriaje(id, "ajustador.uno"));

        Assert.True(r.EsExito);
        Assert.Equal(antes, Foto(b, id));
        Assert.Equal(eventos, b.Almacen.BandejaDeSalida.Count);
        Assert.Equal(EstadoSiniestro.Avisado, b.Almacen.SiniestroGuardado(id)!.Estado);
    }

    /// <summary>
    /// El modelo «afirma» Colisión con una póliza sin Daños materiales. La persona acepta la propuesta: el dominio
    /// calcula que la cobertura no está contratada y el dictamen sale improcedente con GEN-1, diga lo que diga el modelo.
    /// </summary>
    [Fact]
    public async Task Aceptar_una_propuesta_pasa_por_las_reglas_y_el_dictamen_es_del_dominio()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto(conDanosMateriales: false);
        var id = await b.Aviso(folio);
        var propuesta = (await b.Enviar(new ProponerTriaje(id, "ajustador.uno"))).Valor;
        Assert.Equal((TipoDeSiniestro.Colision, false), (propuesta.Tipo, propuesta.CoberturaContratada));

        Exito(await b.Enviar(new TriarSiniestro(id, propuesta.Tipo!.Value, propuesta.Id, "ajustador.uno")));
        Exito(await b.Enviar(new AsignarAjustador(id, await b.Ajustador(Especialidad.Auto))));
        Exito(await b.Enviar(new Dictaminar(id, 80_000m, [], null, "ajustador.uno")));

        var guardado = b.Almacen.SiniestroGuardado(id)!;
        Assert.Equal(propuesta.Id, guardado.PropuestaAceptada);
        Assert.Equal((false, MotivoDeDictamen.CoberturaNoContratada, "GEN-1", 0m),
            (guardado.Dictamen!.Procedente, guardado.Dictamen.Motivo, guardado.Dictamen.Clausula,
                guardado.Dictamen.Indemnizacion));
    }

    [Fact]
    public async Task El_texto_llega_seudonimizado_y_el_detalle_de_lesiones_no_llega()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio, lesiones: "Fractura de muñeca izquierda; atendida en urgencias.");
        await b.Enviar(new ProponerTriaje(id, "ajustador.uno"));

        var texto = Assert.Single(b.Asistente.Recibidas).Descripcion.Texto;
        foreach (var dato in new[] { "Lucía", "Ferrer", "Montaño", "4100", "lucia.ferrer", "Fractura", "muñeca" })
        {
            Assert.DoesNotContain(dato, texto, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("[PERSONA_1]", texto, StringComparison.Ordinal);
        Assert.Contains("[TELEFONO_1]", texto, StringComparison.Ordinal);
        Assert.Contains("[CORREO_1]", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_propuesta_solo_se_pide_antes_del_triaje()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio);
        Exito(await b.Enviar(new TriarSiniestro(id, TipoDeSiniestro.Colision, null, "ajustador.uno")));
        Assert.Equal("ya_triado", (await b.Enviar(new ProponerTriaje(id, "ajustador.uno"))).Error?.Codigo);
        Assert.Empty(b.Asistente.Recibidas);
    }

    [Fact]
    public async Task No_se_puede_triar_citando_la_propuesta_de_otro_siniestro()
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var uno = await b.Aviso(folio);
        var otro = await b.Aviso(folio);
        var ajena = (await b.Enviar(new ProponerTriaje(otro, "ajustador.uno"))).Valor;
        var r = await b.Enviar(new TriarSiniestro(uno, TipoDeSiniestro.Colision, ajena.Id, "ajustador.uno"));
        Assert.Equal("no_encontrado", r.Error?.Codigo);
        Assert.Equal(EstadoSiniestro.Avisado, b.Almacen.SiniestroGuardado(uno)!.Estado);
    }

    private static string Foto(Banco b, Guid id) => JsonSerializer.Serialize(b.Almacen.SiniestroGuardado(id));
}

/// <summary>Las guardas sobre la salida del modelo (casos tomados de lo que hace un modelo chico).</summary>
public class ValidadorDePropuestaTests
{
    private static readonly Domain.Polizas.Poliza Poliza = Domain.Polizas.Poliza.Emitir(Guid.NewGuid(), "AFV-AUT-1",
        Ramo.Auto, Guid.NewGuid(), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31),
        [new(CodigoCobertura.RoboTotal, 1m, new Domain.Polizas.Deducible(Domain.Polizas.TipoDeDeducible.Fijo, 0m), 0m)])
        .Valor;

    private static PropuestaDeTriaje Validar(PropuestaGenerada g) =>
        ValidadorDePropuesta.Validar(g, Guid.NewGuid(), Ramo.Auto, Poliza, Guid.NewGuid(), DateTimeOffset.UnixEpoch,
            "ajustador.uno");

    [Theory]
    [InlineData("3")]
    [InlineData("Colision, Vuelco")]
    [InlineData("colision")]
    [InlineData("DanosPorAgua")]     // tipo de hogar en una póliza de auto
    public void Un_tipo_que_no_es_un_nombre_exacto_del_ramo_se_descarta(string tipo)
    {
        var p = Validar(AsistenteGuionado.Generada(tipo, null));
        Assert.Null(p.Tipo);
        Assert.Contains(p.Observaciones, o => o.StartsWith("tipo descartado", StringComparison.Ordinal));
    }

    [Fact]
    public void La_cita_debe_estar_entre_las_recuperadas_y_amparar_el_tipo()
    {
        Assert.False(Validar(AsistenteGuionado.Generada("RoboTotal", "AUT-2.2")).CitaSustentada);   // no recuperada
        Assert.False(Validar(AsistenteGuionado.Generada("Vuelco", "AUT-2.1-a")).CitaSustentada);    // otro tipo
        Assert.False(Validar(AsistenteGuionado.Generada("Colision", "GEN-7")).CitaSustentada);      // no es cobertura
        var buena = Validar(AsistenteGuionado.Generada("Colision", "AUT-2.1-a"));
        Assert.True(buena.CitaSustentada);
        Assert.Equal("AUT-2.1-a", buena.Clausula);
    }

    [Fact]
    public void La_cobertura_y_si_esta_contratada_las_calcula_la_aplicacion()
    {
        var p = Validar(AsistenteGuionado.Generada("Colision", "AUT-2.1-a"));
        Assert.Equal((CodigoCobertura.DanosMateriales, false), (p.Cobertura, p.CoberturaContratada));
    }

    [Fact]
    public void Solo_quedan_los_datos_que_pide_el_tipo_y_las_exclusiones_recuperadas_del_ramo()
    {
        var p = Validar(AsistenteGuionado.Generada("Colision", "AUT-2.1-a",
            datos: ["Fotografias", "InformeMedico", "Cualquiera"], exclusiones: ["AUT-4.1", "AUT-4.2", "HOG-4.1"],
            senales: ["PresionPorPagoRapido", "Sospechoso"]));
        Assert.Equal(new[] { DatoRequerido.Fotografias }, p.DatosFaltantes);
        Assert.Equal(new[] { "AUT-4.1" }, p.Exclusiones);       // AUT-4.2 no se recuperó; HOG-4.1 es de otro ramo
        Assert.Equal(new[] { SenalDeRiesgo.PresionPorPagoRapido }, p.Senales);
        Assert.Equal(4, p.Observaciones.Count(o => o.Contains("descartad", StringComparison.Ordinal)));
    }

    [Fact]
    public void La_justificacion_se_recorta_a_mil_quinientos_caracteres() =>
        Assert.Equal(1500, Validar(AsistenteGuionado.Generada("Colision", "AUT-2.1-a") with
        {
            Justificacion = new string('a', 5000),
        }).Justificacion.Length);
}

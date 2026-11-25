using Perito.Application.Privacidad;
using Perito.Application.Siniestros;
using Perito.Domain.Ajustadores;
using Perito.Domain.Asegurados;
using Perito.Domain.Producto;
using static Perito.Application.Tests.Banco;

namespace Perito.Application.Tests;

/// <summary>GEN-12 en los casos de uso: finalidad, registro de accesos y anonimización a solicitud.</summary>
public class PrivacidadTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("curiosidad")]
    public async Task Consultar_un_siniestro_exige_una_finalidad_permitida_y_sin_ella_no_registra_ni_devuelve(
        string? finalidad)
    {
        var b = new Banco();
        var (_, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio);
        var r = await b.Enviar(new ConsultarSiniestro(id, "ajustador.uno", "ajustador", finalidad!));
        Assert.Equal("finalidad_invalida", r.Error?.Codigo);
        Assert.Empty(b.Almacen.Accesos);
    }

    [Fact]
    public async Task Cada_consulta_de_datos_personales_queda_registrada_con_quien_que_de_quien_y_para_que()
    {
        var b = new Banco();
        var (titular, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio);
        Exito(await b.Enviar(new ConsultarSiniestro(id, "ajustador.uno", "ajustador",
            Finalidades.AtencionDelSiniestro)));
        Exito(await b.Enviar(new ConsultarAsegurado(titular, "privacidad.uno", "privacidad",
            Finalidades.DerechosDelTitular)));

        var accesos = await b.Enviar(new ConsultarAccesos(titular));
        Assert.Equal(
            [("ajustador.uno", "siniestro", id, Finalidades.AtencionDelSiniestro),
                ("privacidad.uno", "asegurado", titular, Finalidades.DerechosDelTitular)],
            accesos.Select(a => (a.Usuario, a.Recurso, a.RecursoId, a.Finalidad)));
        Assert.All(accesos, a => Assert.Equal(titular, a.TitularId));
    }

    [Fact]
    public async Task Con_un_siniestro_abierto_no_se_anonimiza()
    {
        var b = new Banco();
        var (titular, folio) = await b.PolizaAuto();
        await b.Aviso(folio);
        var r = await b.Enviar(new AnonimizarAsegurado(titular, "privacidad.uno", "privacidad"));
        Assert.Equal("siniestros_abiertos", r.Error?.Codigo);
    }

    [Fact]
    public async Task Anonimizar_borra_al_titular_y_el_texto_libre_conserva_los_montos_y_queda_registrado()
    {
        var b = new Banco();
        var (titular, folio) = await b.PolizaAuto();
        var id = await b.Aviso(folio, lesiones: "Contusión leve.");
        Exito(await b.Enviar(new TriarSiniestro(id, TipoDeSiniestro.Colision, null, "ajustador.uno")));
        Exito(await b.Enviar(new AsignarAjustador(id, await b.Ajustador(Especialidad.Auto))));
        foreach (var dato in Catalogo.De(TipoDeSiniestro.Colision).Datos)
        {
            Exito(await b.Enviar(new RegistrarDatoRecibido(id, dato)));
        }

        Exito(await b.Enviar(new Dictaminar(id, 1_000m, [CodigoExclusion.Competencias], null, "ajustador.uno")));
        Exito(await b.Enviar(new RechazarSiniestro(id)));

        Exito(await b.Enviar(new AnonimizarAsegurado(titular, "privacidad.uno", "privacidad")));

        var vista = (await b.Enviar(new ConsultarAsegurado(titular, "privacidad.uno", "privacidad",
            Finalidades.Auditoria))).Valor;
        Assert.Equal((Asegurado.NombreAnonimizado, null, null, null, null),
            (vista.Nombre, vista.Rfc, vista.Correo, vista.Telefono, vista.Clabe));
        var siniestro = b.Almacen.SiniestroGuardado(id)!;
        Assert.Null(siniestro.Descripcion);
        Assert.Null(siniestro.DetalleDeLesiones);
        Assert.Equal("AUT-4.4", siniestro.Dictamen!.Clausula);         // lo financiero y su fundamento se conservan
        Assert.Contains(b.Almacen.BandejaDeSalida, e => e is AseguradoAnonimizado x && x.AseguradoId == titular);
        Assert.Contains(b.Almacen.Accesos, a => a.Finalidad == Finalidades.Anonimizacion && a.TitularId == titular);
    }
}

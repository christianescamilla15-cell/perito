using Perito.Domain.Asegurados;
using Perito.Domain.Privacidad;

namespace Perito.Domain.Tests;

/// <summary>Minimización y validación de los datos del titular, anonimización (GEN-12) y seudonimización.</summary>
public class PrivacidadTests
{
    private static Asegurado Lucia() => Asegurado.Registrar(Guid.NewGuid(), "Lucía Ferrer Montaño", "FEML850101AB1",
        "lucia.ferrer@example.com", "55 4100 2233", "002180000000000012").Valor;

    [Theory]
    [InlineData("rfc", "rfc_invalido")]
    [InlineData("correo", "correo_invalido")]
    [InlineData("telefono", "telefono_invalido")]
    [InlineData("clabe", "clabe_invalida")]
    [InlineData("nombre", "nombre_invalido")]
    public void Cada_dato_del_titular_se_valida(string campo, string codigo)
    {
        var r = Asegurado.Registrar(Guid.NewGuid(), campo == "nombre" ? "Lu" : "Lucía Ferrer",
            campo == "rfc" ? "FEML85010" : "FEML850101AB1", campo == "correo" ? "lucia-sin-arroba" : "l@example.com",
            campo == "telefono" ? "55 4100 223" : "5541002233", campo == "clabe" ? "002180000000000013" : null);
        Assert.Equal(codigo, r.Error?.Codigo);
    }

    [Theory]
    [InlineData("002180000000000012", true)]
    [InlineData("014180098765432103", true)]
    [InlineData("002180000000000013", false)]   // dígito de control equivocado
    [InlineData("00218000000000001", false)]    // 17 dígitos
    [InlineData("00218000000000001A", false)]
    public void La_clabe_se_valida_con_su_digito_de_control(string clabe, bool valida) =>
        Assert.Equal(valida, Asegurado.ClabeEsValida(clabe));

    [Fact]
    public void El_telefono_se_guarda_solo_con_sus_diez_digitos() =>
        Assert.Equal("5541002233", Lucia().Telefono);

    [Fact]
    public void Con_siniestros_abiertos_no_se_anonimiza()
    {
        var a = Lucia();
        Assert.Equal("siniestros_abiertos", a.Anonimizar(1, DateTimeOffset.UnixEpoch).Error?.Codigo);
        Assert.Equal("Lucía Ferrer Montaño", a.Nombre);
    }

    [Fact]
    public void Anonimizar_borra_todo_identificador_es_irreversible_y_emite_su_evento()
    {
        var a = Lucia();
        Assert.True(a.Anonimizar(0, DateTimeOffset.UnixEpoch).EsExito);
        Assert.Equal((Asegurado.NombreAnonimizado, null, null, null, null),
            (a.Nombre, a.Rfc, a.Correo, a.Telefono, a.Clabe));
        Assert.Empty(a.Identificadores());
        Assert.IsType<AseguradoAnonimizado>(Assert.Single(a.Eventos));
        Assert.Equal("ya_anonimizado", a.Anonimizar(0, DateTimeOffset.UnixEpoch).Error?.Codigo);
    }

    [Fact]
    public void El_nombre_conocido_se_reemplaza_completo_por_partes_y_sin_acentos_con_la_misma_marca()
    {
        var t = Seudonimizador.Seudonimizar(
            "Soy Lucía Ferrer Montaño. LUCIA FERRER avisó; Lucia confirma.", Lucia().Identificadores());
        Assert.Equal("Soy [PERSONA_1]. [PERSONA_1] avisó; [PERSONA_1] confirma.", t.Texto);
        Assert.Equal(1, t.ConteoPorCategoria["PERSONA"]);
    }

    [Theory]
    [InlineData("mi teléfono es 55 4100 2233", "mi teléfono es [TELEFONO_1]")]
    [InlineData("mi teléfono es 55-4100-2233", "mi teléfono es [TELEFONO_1]")]
    [InlineData("mi teléfono es 5541002233", "mi teléfono es [TELEFONO_1]")]
    [InlineData("escríbanme a lucia.ferrer@example.com", "escríbanme a [CORREO_1]")]
    [InlineData("RFC FEML850101AB1", "RFC [RFC_1]")]
    [InlineData("CLABE 002180000000000012", "CLABE [CLABE_1]")]
    public void Los_identificadores_conocidos_del_titular_no_pasan(string texto, string esperado) =>
        Assert.Equal(esperado, Seudonimizador.Seudonimizar(texto, Lucia().Identificadores()).Texto);

    [Theory]
    [InlineData("su correo es otro.tercero@example.net", "su correo es [CORREO_1]")]
    [InlineData("tel. 33 1234 5678", "tel. [TELEFONO_1]")]
    [InlineData("CURP BAPM900101HDFRRR09", "CURP [CURP_1]")]
    [InlineData("RFC del tercero QUMA750505T12", "RFC del tercero [RFC_1]")]
    [InlineData("tarjeta 4111 1111 1111 1111", "tarjeta [TARJETA_1]")]
    [InlineData("carpeta de investigación CI-FVL-2026-0042", "carpeta de investigación [FOLIO_1]")]
    [InlineData("estacionado en la calle Pino Suárez 115 de noche", "estacionado en la [DIRECCION_1] de noche")]
    [InlineData("el vecino se llama Arturo Salcedo y", "el vecino se llama [PERSONA_1] y")]
    [InlineData("la vecina, la señora Marisol Quintero, del 302", "la vecina, la señora [PERSONA_1], del 302")]
    [InlineData("el vecino, Ernesto Paredes, nos pasó", "el vecino, [PERSONA_1], nos pasó")]
    public void Los_datos_de_terceros_se_reconocen_por_patron_o_por_senal(string texto, string esperado) =>
        Assert.Equal(esperado, Seudonimizador.Seudonimizar(texto, []).Texto);

    [Fact]
    public void Un_texto_sin_datos_personales_no_cambia_ni_sus_montos_ni_sus_fechas()
    {
        const string texto = "Cayó granizo el 12 de marzo a las 22:30; el presupuesto es de 12,345.67 pesos (km 32).";
        var t = Seudonimizador.Seudonimizar(texto, Lucia().Identificadores());
        Assert.Equal(texto, t.Texto);
        Assert.Empty(t.ConteoPorCategoria);
    }

    [Fact]
    public void El_mismo_valor_recibe_la_misma_marca_y_valores_distintos_marcas_distintas()
    {
        var t = Seudonimizador.Seudonimizar("a@example.com, b@example.com y otra vez a@example.com", []);
        Assert.Equal("[CORREO_1], [CORREO_2] y otra vez [CORREO_1]", t.Texto);
    }
}

using System.Security.Cryptography;
using System.Text;
using Perito.Infrastructure.Privacidad;

namespace Perito.Infrastructure.Tests;

/// <summary>Cifrado de campo AES-256-GCM: confidencialidad, integridad, vínculo al campo y rotación de llaves.</summary>
public class CifradoTests
{
    private static string Llave(byte semilla) =>
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(i + semilla)).ToArray());

    private static readonly CifradorDeCampos Cifrador =
        new(new OpcionesDeCifrado("k1", new Dictionary<string, string> { ["k1"] = Llave(1) }));

    [Fact]
    public void Lo_cifrado_se_descifra_igual_y_no_contiene_el_texto_claro()
    {
        var cifrado = Cifrador.Cifrar("FEML850101AB1", "asegurados.rfc");
        Assert.Equal("FEML850101AB1", Cifrador.Descifrar(cifrado, "asegurados.rfc"));
        Assert.DoesNotContain("FEML", Encoding.Latin1.GetString(cifrado), StringComparison.Ordinal);
    }

    [Fact]
    public void El_mismo_texto_cifrado_dos_veces_da_valores_distintos() =>
        Assert.NotEqual(Cifrador.Cifrar("igual", "c"), Cifrador.Cifrar("igual", "c"));

    [Fact]
    public void Un_valor_de_un_campo_no_se_descifra_como_otro_campo() =>
        Assert.ThrowsAny<CryptographicException>(() =>
            Cifrador.Descifrar(Cifrador.Cifrar("dato", "asegurados.rfc"), "asegurados.correo"));

    [Fact]
    public void Un_valor_alterado_no_se_descifra()
    {
        var cifrado = Cifrador.Cifrar("dato", "c");
        cifrado[^1] ^= 0x01;
        Assert.ThrowsAny<CryptographicException>(() => Cifrador.Descifrar(cifrado, "c"));
    }

    [Fact]
    public void Al_rotar_lo_viejo_se_descifra_y_lo_nuevo_usa_la_llave_activa()
    {
        var viejo = Cifrador.Cifrar("dato", "c");
        var rotado = new CifradorDeCampos(new OpcionesDeCifrado("k2",
            new Dictionary<string, string> { ["k1"] = Llave(1), ["k2"] = Llave(50) }));
        Assert.Equal("dato", rotado.Descifrar(viejo, "c"));
        Assert.Equal("k2", CifradorDeCampos.LlaveDe(rotado.Cifrar("dato", "c")));
        var sinLaVieja = new CifradorDeCampos(new OpcionesDeCifrado("k2",
            new Dictionary<string, string> { ["k2"] = Llave(50) }));
        Assert.ThrowsAny<CryptographicException>(() => sinLaVieja.Descifrar(viejo, "c"));
    }

    [Theory]
    [InlineData("k9", 32)]     // la activa no está en el anillo
    [InlineData("k1", 16)]     // llave de 128 bits
    public void Sin_un_anillo_valido_no_arranca(string activa, int bytes) =>
        Assert.Throws<ArgumentException>(() => new CifradorDeCampos(new OpcionesDeCifrado(activa,
            new Dictionary<string, string> { ["k1"] = Convert.ToBase64String(new byte[bytes]) })));
}

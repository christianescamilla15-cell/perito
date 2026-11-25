using System.Text.RegularExpressions;
using Perito.Domain.Comun;

namespace Perito.Domain.Asegurados;

/// <summary>
/// Titular de pólizas. Minimización: sólo los datos que el siniestro necesita (nombre, RFC, correo, teléfono y la
/// CLABE para pagar); ni fecha de nacimiento ni domicilio. La infraestructura cifra el RFC, el correo, el teléfono y la
/// CLABE; el dominio no sabe de cifrado.
/// </summary>
public sealed partial class Asegurado : RaizDeAgregado
{
    /// <summary>Lo que queda del nombre después de anonimizar.</summary>
    public const string NombreAnonimizado = "ANONIMIZADO";

    private Asegurado(Guid id, string nombre, string? rfc, string? correo, string? telefono, string? clabe,
        DateTimeOffset? anonimizadoEn)
    {
        Id = id;
        Nombre = nombre;
        Rfc = rfc;
        Correo = correo;
        Telefono = telefono;
        Clabe = clabe;
        AnonimizadoEn = anonimizadoEn;
    }

    /// <summary>Nombre completo.</summary>
    public string Nombre { get; private set; }

    /// <summary>RFC (identificador fiscal); nulo después de anonimizar.</summary>
    public string? Rfc { get; private set; }

    /// <summary>Correo de contacto; nulo después de anonimizar.</summary>
    public string? Correo { get; private set; }

    /// <summary>Teléfono de 10 dígitos; nulo después de anonimizar.</summary>
    public string? Telefono { get; private set; }

    /// <summary>CLABE para el pago; nula si no se ha dado o después de anonimizar.</summary>
    public string? Clabe { get; private set; }

    /// <summary>Cuándo se anonimizó, si se hizo.</summary>
    public DateTimeOffset? AnonimizadoEn { get; private set; }

    /// <summary>Registra un titular validando cada dato.</summary>
    public static Resultado<Asegurado> Registrar(Guid id, string nombre, string rfc, string correo, string telefono,
        string? clabe)
    {
        nombre = (nombre ?? string.Empty).Trim();
        if (nombre.Length is < 3 or > 120)
        {
            return Resultado<Asegurado>.Fallo("nombre_invalido", "El nombre tiene de 3 a 120 caracteres.");
        }

        rfc = (rfc ?? string.Empty).Trim().ToUpperInvariant();
        if (!PatronRfc().IsMatch(rfc))
        {
            return Resultado<Asegurado>.Fallo("rfc_invalido", "El RFC no tiene el formato de 12 o 13 caracteres.");
        }

        correo = (correo ?? string.Empty).Trim();
        if (correo.Length > 254 || !PatronCorreo().IsMatch(correo))
        {
            return Resultado<Asegurado>.Fallo("correo_invalido", "El correo no es válido.");
        }

        var digitos = new string((telefono ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (digitos.Length != 10)
        {
            return Resultado<Asegurado>.Fallo("telefono_invalido", "El teléfono tiene 10 dígitos.");
        }

        if (clabe is not null && !ClabeEsValida(clabe))
        {
            return Resultado<Asegurado>.Fallo("clabe_invalida", "La CLABE tiene 18 dígitos y su dígito de control.");
        }

        return Resultado<Asegurado>.Exito(new Asegurado(id, nombre, rfc, correo, digitos, clabe, null));
    }

    /// <summary>Reconstruye un titular guardado.</summary>
    public static Asegurado Rehidratar(Guid id, string nombre, string? rfc, string? correo, string? telefono,
        string? clabe, DateTimeOffset? anonimizadoEn) =>
        new(id, nombre, rfc, correo, telefono, clabe, anonimizadoEn);

    /// <summary>
    /// Anonimiza a solicitud del titular (GEN-12): irreversible. No procede mientras tenga siniestros abiertos, porque
    /// sus datos hacen falta para pagar o comunicar el rechazo.
    /// </summary>
    public Resultado Anonimizar(int siniestrosAbiertos, DateTimeOffset ahora)
    {
        if (AnonimizadoEn is not null)
        {
            return Resultado.Fallo("ya_anonimizado", "El titular ya estaba anonimizado.");
        }

        if (siniestrosAbiertos > 0)
        {
            return Resultado.Fallo("siniestros_abiertos",
                $"Tiene {siniestrosAbiertos} siniestro(s) abierto(s): se anonimiza al cerrarlos.");
        }

        Nombre = NombreAnonimizado;
        Rfc = null;
        Correo = null;
        Telefono = null;
        Clabe = null;
        AnonimizadoEn = ahora;
        Registrar(new AseguradoAnonimizado(Guid.CreateVersion7(ahora), ahora, Id));
        return Resultado.Exito();
    }

    /// <summary>Los datos que identifican al titular, para seudonimizar un texto antes de un prompt.</summary>
    public IReadOnlyList<string> Identificadores() =>
        new[] { Nombre, Rfc, Correo, Telefono, Clabe }
            .Where(v => !string.IsNullOrWhiteSpace(v) && v != NombreAnonimizado).Select(v => v!).ToList();

    /// <summary>18 dígitos; el último es el de control (pesos 3, 7, 1).</summary>
    public static bool ClabeEsValida(string clabe)
    {
        if (clabe.Length != 18 || !clabe.All(char.IsAsciiDigit))
        {
            return false;
        }

        int[] pesos = [3, 7, 1];
        var suma = 0;
        for (var i = 0; i < 17; i++)
        {
            suma += (clabe[i] - '0') * pesos[i % 3] % 10;
        }

        return (10 - suma % 10) % 10 == clabe[17] - '0';
    }

    [GeneratedRegex(@"^[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronRfc();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronCorreo();
}

/// <summary>El titular se anonimizó (sólo su identificador).</summary>
public sealed record AseguradoAnonimizado(Guid Id, DateTimeOffset OcurridoEn, Guid AseguradoId) : IEventoDeDominio;

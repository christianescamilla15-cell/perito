using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Perito.Infrastructure.Privacidad;

/// <summary>Llaves de cifrado de campos: un anillo por id y la activa. Vienen de la configuración (en producción, de un
/// almacén de secretos); nunca del código.</summary>
public sealed record OpcionesDeCifrado(string LlaveActiva, IReadOnlyDictionary<string, string> LlavesBase64);

/// <summary>
/// Cifrado de campo con AES-256-GCM. Formato: versión (1 byte) · largo del id de llave (1) · id · nonce (12) ·
/// etiqueta (16) · texto cifrado. El dato adicional autenticado es el nombre del campo: un valor cifrado de una
/// columna no se puede copiar a otra. Descifra con cualquier llave del anillo (rotación); cifra con la activa.
/// </summary>
public sealed class CifradorDeCampos
{
    private const byte Version = 1;
    private readonly Dictionary<string, byte[]> _llaves;
    private readonly string _activa;

    /// <summary>Valida el anillo: cada llave de 32 bytes y la activa presente. Sin llaves válidas no arranca.</summary>
    public CifradorDeCampos(OpcionesDeCifrado opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _llaves = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var (id, base64) in opciones.LlavesBase64)
        {
            if (string.IsNullOrWhiteSpace(id) || Encoding.UTF8.GetByteCount(id) > 32)
            {
                throw new ArgumentException("Id de llave vacío o de más de 32 bytes.", nameof(opciones));
            }

            var llave = Convert.FromBase64String(base64);
            _llaves[id] = llave.Length == 32
                ? llave
                : throw new ArgumentException($"La llave {id} no mide 32 bytes.", nameof(opciones));
        }

        _activa = _llaves.ContainsKey(opciones.LlaveActiva)
            ? opciones.LlaveActiva
            : throw new ArgumentException("La llave activa no está en el anillo.", nameof(opciones));
    }

    /// <summary>Cifra un texto para un campo.</summary>
    public byte[] Cifrar(string texto, string campo)
    {
        var id = Encoding.UTF8.GetBytes(_activa);
        var claro = Encoding.UTF8.GetBytes(texto);
        var salida = new byte[2 + id.Length + 12 + 16 + claro.Length];
        salida[0] = Version;
        salida[1] = (byte)id.Length;
        id.CopyTo(salida, 2);
        var nonce = salida.AsSpan(2 + id.Length, 12);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_llaves[_activa], 16);
        aes.Encrypt(nonce, claro, salida.AsSpan(2 + id.Length + 28), salida.AsSpan(2 + id.Length + 12, 16),
            Adicional(campo));
        return salida;
    }

    /// <summary>Descifra; un valor alterado, de otro campo o con una llave fuera del anillo lanza
    /// <see cref="CryptographicException"/>.</summary>
    public string Descifrar(byte[] datos, string campo)
    {
        ArgumentNullException.ThrowIfNull(datos);
        if (datos.Length < 2 || datos[0] != Version || datos.Length < 2 + datos[1] + 28)
        {
            throw new CryptographicException("Formato de campo cifrado desconocido.");
        }

        var id = Encoding.UTF8.GetString(datos, 2, datos[1]);
        if (!_llaves.TryGetValue(id, out var llave))
        {
            throw new CryptographicException($"La llave {id} no está en el anillo.");
        }

        var inicio = 2 + datos[1];
        var claro = new byte[datos.Length - inicio - 28];
        using var aes = new AesGcm(llave, 16);
        aes.Decrypt(datos.AsSpan(inicio, 12), datos.AsSpan(inicio + 28), datos.AsSpan(inicio + 12, 16), claro,
            Adicional(campo));
        return Encoding.UTF8.GetString(claro);
    }

    /// <summary>El id de la llave con que se cifró un valor (para re-cifrar al rotar).</summary>
    public static string LlaveDe(byte[] datos) => Encoding.UTF8.GetString(datos, 2, datos[1]);

    private static byte[] Adicional(string campo)
    {
        var bytes = Encoding.UTF8.GetBytes("perito:" + campo);
        var salida = new byte[4 + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(salida, bytes.Length);
        bytes.CopyTo(salida, 4);
        return salida;
    }
}

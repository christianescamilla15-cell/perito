using System.Text;
using System.Text.RegularExpressions;

namespace Perito.Domain.Privacidad;

/// <summary>
/// Texto ya seudonimizado: la ÚNICA forma de entregarle texto libre al asistente. No tiene constructor público; sólo
/// <see cref="Seudonimizador.Seudonimizar"/> lo crea, así que un prompt con datos personales en claro no compila.
/// </summary>
public sealed class TextoSeudonimizado
{
    internal TextoSeudonimizado(string texto, IReadOnlyDictionary<string, int> conteo)
    {
        Texto = texto;
        ConteoPorCategoria = conteo;
    }

    /// <summary>El texto con cada dato personal reemplazado por una marca como [PERSONA_1].</summary>
    public string Texto { get; }

    /// <summary>Cuántos valores distintos se reemplazaron por categoría (para auditar sin guardar los valores).</summary>
    public IReadOnlyDictionary<string, int> ConteoPorCategoria { get; }

    /// <inheritdoc />
    public override string ToString() => Texto;
}

/// <summary>
/// Seudonimización antes de cualquier prompt (GEN-12): primero los identificadores CONOCIDOS del titular (nombre y
/// sus partes, RFC, correo, teléfono, CLABE); luego patrones (correos, CURP, RFC, CLABE, tarjetas, teléfonos, folios,
/// direcciones con número) y nombres de terceros precedidos de una señal («se llama», «la señora», «el vecino,»).
/// Un mismo valor recibe siempre la misma marca. Límite conocido: un nombre de tercero sin señal no se detecta
/// (etapa 2: reconocimiento de entidades).
/// </summary>
public static partial class Seudonimizador
{
    /// <summary>Seudonimiza un texto con los identificadores conocidos de su titular.</summary>
    public static TextoSeudonimizado Seudonimizar(string texto, IEnumerable<string> identificadoresConocidos)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var marcas = new Marcas();
        var resultado = texto;

        foreach (var (patron, categoria, clave) in Variantes(identificadoresConocidos))
        {
            resultado = patron.Replace(resultado, _ => marcas.Para(categoria, clave));
        }

        resultado = Correo().Replace(resultado, m => marcas.Para("CORREO", m.Value));
        resultado = Folio().Replace(resultado, m => marcas.Para("FOLIO", m.Value));
        resultado = Curp().Replace(resultado, m => marcas.Para("CURP", m.Value));
        resultado = Rfc().Replace(resultado, m => marcas.Para("RFC", m.Value));
        resultado = Clabe().Replace(resultado, m => marcas.Para("CLABE", m.Value));
        resultado = Tarjeta().Replace(resultado, m => marcas.Para("TARJETA", Digitos(m.Value)));
        resultado = Telefono().Replace(resultado, m => marcas.Para("TELEFONO", Digitos(m.Value)));
        resultado = Direccion().Replace(resultado, m => marcas.Para("DIRECCION", m.Value));
        resultado = NombreConSenal().Replace(resultado,
            m => m.Groups["senal"].Value + marcas.Para("PERSONA", m.Groups["nombre"].Value));
        return new TextoSeudonimizado(resultado, marcas.Conteo());
    }

    private static IEnumerable<(Regex Patron, string Categoria, string Clave)> Variantes(IEnumerable<string> conocidos)
    {
        var lista = new List<(string Patron, string Categoria, string Clave, int Largo)>();
        foreach (var valor in conocidos.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()))
        {
            var digitos = Digitos(valor);
            if (valor.Contains('@'))
            {
                lista.Add((Regex.Escape(valor), "CORREO", valor, valor.Length));
            }
            else if (digitos.Length == 18 && digitos.Length == valor.Length)
            {
                lista.Add((Regex.Escape(valor), "CLABE", valor, valor.Length));
            }
            else if (digitos.Length == 10 && valor.All(c => char.IsAsciiDigit(c) || c is ' ' or '-'))
            {
                // El teléfono conocido se busca con cualquier separador: 5541002233, 55 4100 2233, 55-4100-2233.
                lista.Add((@"(?<!\d)" + string.Join(@"[\s-]?", digitos.Select(c => c.ToString())) + @"(?!\d)",
                    "TELEFONO", digitos, valor.Length));
            }
            else if (Rfc().IsMatch(valor))
            {
                lista.Add((Regex.Escape(valor), "RFC", valor, valor.Length));
            }
            else
            {
                // El nombre completo, cada par de palabras seguidas y cada palabra de 4 letras o más: todas con la
                // misma marca, porque son la misma persona.
                var palabras = valor.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var partes = new List<string> { valor };
                for (var i = 0; i + 1 < palabras.Length; i++)
                {
                    partes.Add(palabras[i] + " " + palabras[i + 1]);
                }

                partes.AddRange(palabras.Where(p => p.Length >= 4));
                lista.AddRange(partes.Distinct().Select(p =>
                    ($@"(?<!\p{{L}}){PatronFlexible(p)}(?!\p{{L}})", "PERSONA", valor, p.Length)));
            }
        }

        // Lo más largo primero: el nombre completo antes que sus partes.
        return lista.OrderByDescending(v => v.Largo)
            .Select(v => (new Regex(v.Patron,
                (v.Categoria == "PERSONA" ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)), v.Categoria, v.Clave))
            .ToList();
    }

    private static string PatronFlexible(string valor)
    {
        // Igual sin importar acentos ni mayúsculas, salvo la inicial de cada palabra, que va en mayúscula como en un
        // nombre propio: «Lucia» y «LUCÍA» son «Lucía», pero «ríos» (de un arroyo) no es «Ríos».
        var sb = new StringBuilder();
        var inicioDePalabra = true;
        foreach (var c in valor)
        {
            if (char.IsLetter(c))
            {
                var baseLetra = QuitarAcento(c);
                var mayusculas = char.ToUpperInvariant(baseLetra) + Acentuadas(baseLetra).ToUpperInvariant();
                sb.Append('[').Append(mayusculas);
                if (!inicioDePalabra)
                {
                    sb.Append(char.ToLowerInvariant(baseLetra)).Append(Acentuadas(baseLetra).ToLowerInvariant());
                }

                sb.Append(']');
                inicioDePalabra = false;
            }
            else if (char.IsWhiteSpace(c))
            {
                sb.Append(@"\s+");
                inicioDePalabra = true;
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
                inicioDePalabra = true;
            }
        }

        return sb.ToString();
    }

    // Sin Normalize: con InvariantGlobalization no descompone los acentos. El español basta con esta tabla.
    private static char QuitarAcento(char c) => char.ToLowerInvariant(c) switch
    {
        'á' => 'a',
        'é' => 'e',
        'í' => 'i',
        'ó' => 'o',
        'ú' or 'ü' => 'u',
        'ñ' => 'n',
        _ => char.ToLowerInvariant(c),
    };

    private static string Acentuadas(char baseLetra) => char.ToLowerInvariant(baseLetra) switch
    {
        'a' => "á",
        'e' => "é",
        'i' => "í",
        'o' => "ó",
        'u' => "úü",
        'n' => "ñ",
        _ => string.Empty,
    };

    private static string Digitos(string valor) => new(valor.Where(char.IsAsciiDigit).ToArray());

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Correo();

    [GeneratedRegex(@"\b[A-Z]{2,5}(?:-[A-Z0-9]{2,8}){2,5}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Folio();

    [GeneratedRegex(@"\b[A-Z]{4}\d{6}[HM][A-Z]{5}[A-Z0-9]\d\b", RegexOptions.CultureInvariant)]
    private static partial Regex Curp();

    [GeneratedRegex(@"\b[A-ZÑ&]{3,4}\d{6}[A-Z0-9]{3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Rfc();

    [GeneratedRegex(@"\b\d{18}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Clabe();

    [GeneratedRegex(@"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Tarjeta();

    [GeneratedRegex(@"(?<!\d)(?:\+?52[\s-]?)?\d{2,3}[\s-]?\d{3,4}[\s-]?\d{4}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex Telefono();

    [GeneratedRegex(
        @"\b(?i:calle|avenida|av\.|calzada|privada|andador|boulevard|blvd\.?|callejón|cerrada|camino)\s+" +
        @"(?:[A-ZÁÉÍÓÚÑ0-9][\wáéíóúñ\.]*\s+){1,4}(?:(?i:n[uú]m(?:ero)?\.?)\s*|#\s*)?\d{1,5}\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex Direccion();

    [GeneratedRegex(
        @"(?<senal>\b(?i:se\s+llama|me\s+llamo|mi\s+nombre\s+es|soy|el\s+señor|la\s+señora|el\s+sr\.|la\s+sra\.|" +
        @"el\s+vecino,?|la\s+vecina,?|el\s+conductor,?|la\s+conductora,?)\s+)" +
        @"(?<nombre>[A-ZÁÉÍÓÚÑ][a-záéíóúñ]+(?:\s+(?:de\s+(?:la\s+)?)?[A-ZÁÉÍÓÚÑ][a-záéíóúñ]+){0,3})",
        RegexOptions.CultureInvariant)]
    private static partial Regex NombreConSenal();

    private sealed class Marcas
    {
        private readonly Dictionary<(string, string), string> _porValor = [];
        private readonly Dictionary<string, int> _porCategoria = [];

        public string Para(string categoria, string valor)
        {
            var llave = (categoria, valor.ToUpperInvariant());
            if (_porValor.TryGetValue(llave, out var marca))
            {
                return marca;
            }

            var n = _porCategoria.GetValueOrDefault(categoria) + 1;
            _porCategoria[categoria] = n;
            marca = $"[{categoria}_{n}]";
            _porValor[llave] = marca;
            return marca;
        }

        public IReadOnlyDictionary<string, int> Conteo() => new Dictionary<string, int>(_porCategoria);
    }
}

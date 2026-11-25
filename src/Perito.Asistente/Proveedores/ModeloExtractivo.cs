using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Perito.Asistente.Corpus;
using Perito.Asistente.Recuperacion;

namespace Perito.Asistente.Proveedores;

/// <summary>
/// Línea base DETERMINISTA que se comporta como un servicio de chat de Semantic Kernel: no es un LLM. Sólo lee el
/// prompt (descripción seudonimizada y cláusulas recuperadas) y el resultado de la función de la póliza, igual que un
/// modelo. Primero pide la función; después elige la cláusula de cobertura con más raíces en común con la descripción
/// (ponderadas por lo raras que son dentro del contexto), toma su tipo, resta de sus datos los que el texto dice tener
/// (con negación por oración), marca exclusiones con dos o más raíces propias en común y busca señales con frases.
/// Es la compuerta del CI y la línea base contra la que se mide un LLM; sin contexto, no sabe nada.
/// </summary>
public sealed partial class ModeloExtractivo : IChatCompletionService
{
    /// <summary>Nombre del modelo en las propuestas.</summary>
    public const string Nombre = "extractivo-v1";

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var resultado = chatHistory.SelectMany(m => m.Items.OfType<FunctionResultContent>()).LastOrDefault();
        var puedeLlamar = executionSettings?.FunctionChoiceBehavior is not null &&
                          kernel?.Plugins.TryGetFunction(Prompt.Complemento, Prompt.Funcion, out _) == true;
        if (resultado is null && puedeLlamar)
        {
            var llamada = new ChatMessageContent(AuthorRole.Assistant, content: null);
            llamada.Items.Add(new FunctionCallContent(Prompt.Funcion, Prompt.Complemento, "llamada-1",
                new KernelArguments()));
            return Task.FromResult<IReadOnlyList<ChatMessageContent>>([llamada]);
        }

        var usuario = chatHistory.LastOrDefault(m => m.Role == AuthorRole.User)?.Content ?? string.Empty;
        var (descripcion, clausulas) = Prompt.Leer(usuario);
        var json = Proponer(descripcion, clausulas, Contratadas(resultado?.Result as string));
        return Task.FromResult<IReadOnlyList<ChatMessageContent>>([new ChatMessageContent(AuthorRole.Assistant, json)]);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory, PromptExecutionSettings? executionSettings = null, Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var m in await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken))
        {
            yield return new StreamingChatMessageContent(m.Role, m.Content);
        }
    }

    private static string Proponer(string descripcion, IReadOnlyList<Fragmento> clausulas, ISet<string> contratadas)
    {
        var raices = Texto.Raices(descripcion).ToHashSet(StringComparer.Ordinal);
        var porFragmento = clausulas.ToDictionary(f => f.Id,
            f => Texto.Raices(f.Titulo + " " + f.Texto).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
        var df = porFragmento.Values.SelectMany(r => r).GroupBy(r => r, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        double Peso(IEnumerable<string> comunes) => comunes.Sum(r => 1.0 / df[r]);

        var mejor = clausulas.Where(f => f.EsCobertura)
            .Select((f, orden) => (f, Puntaje: Peso(raices.Intersect(porFragmento[f.Id])), orden))
            .Where(x => x.Puntaje > 0)
            .OrderByDescending(x => x.Puntaje)
            .ThenByDescending(x => contratadas.Contains(x.f.Cobertura!))
            .ThenBy(x => x.orden)
            .Select(x => x.f).FirstOrDefault();

        var normal = Texto.Normalizar(descripcion);
        var faltantes = mejor?.Datos.Where(d => !Presente(d, normal)).ToList() ?? [];
        var exclusiones = mejor is null
            ? []
            : clausulas.Where(f => f.EsExclusion && f.Aplica.Contains(mejor.Cobertura!))
                .Where(f => raices.Intersect(porFragmento[f.Id]).Count(r => !porFragmento[mejor.Id].Contains(r)) >= 2)
                .Select(f => f.Id).ToList();
        var senales = Senales.Where(s => s.Patron.IsMatch(normal)).Select(s => s.Nombre).ToList();
        var justificacion = mejor is null
            ? "Ninguna cláusula recuperada coincide con la descripción."
            : $"La descripción coincide con «{mejor.Titulo}» ({mejor.Id}); la cobertura {mejor.Cobertura} " +
              (contratadas.Contains(mejor.Cobertura!) ? "aparece en la carátula." : "no aparece en la carátula.");

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tipo"] = mejor?.Tipo,
            ["clausula"] = mejor?.Id,
            ["datos_faltantes"] = faltantes,
            ["exclusiones"] = exclusiones,
            ["senales"] = senales,
            ["justificacion"] = justificacion,
        });
    }

    private static HashSet<string> Contratadas(string? json)
    {
        var contratadas = new HashSet<string>(StringComparer.Ordinal);
        if (json is null)
        {
            return contratadas;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("coberturas", out var lista) && lista.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in lista.EnumerateArray())
                {
                    if (c.TryGetProperty("codigo", out var codigo) && codigo.ValueKind == JsonValueKind.String)
                    {
                        contratadas.Add(codigo.GetString()!);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Un resultado ilegible equivale a no saber qué está contratado: el desempate por carátula no aplica.
        }

        return contratadas;
    }

    /// <summary>El texto dice que el dato YA se tiene: hay un indicio sin una negación en los 25 caracteres anteriores
    /// de la misma frase (hasta la última coma, dos puntos o punto).</summary>
    private static bool Presente(string dato, string normal)
    {
        if (!Indicios.TryGetValue(dato, out var patron))
        {
            return false;
        }

        foreach (Match m in patron.Matches(normal))
        {
            var inicioDeFrase = normal.LastIndexOfAny(['.', ';', '!', '?', ',', ':'], Math.Max(0, m.Index - 1)) + 1;
            var antes = normal[Math.Max(inicioDeFrase, m.Index - 25)..m.Index];
            if (!Negacion().IsMatch(antes))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly Dictionary<string, Regex> Indicios = new(StringComparer.Ordinal)
    {
        ["Lugar"] = new(@"\b(calle|avenida|av\.|calzada|carretera|autopista|periferico|esquina|cruce|colonia|km|" +
                        @"kilometro|estacionamiento|glorieta|circuito|boulevard)\b|\[direccion_\d+\]",
            RegexOptions.CultureInvariant),
        ["Fotografias"] = new(@"\b(fotos?|fotografias?|videos?|imagenes)\b", RegexOptions.CultureInvariant),
        ["PresupuestoDeReparacion"] = new(@"\b(presupuesto|cotizacion|cotizaron)\b", RegexOptions.CultureInvariant),
        ["DictamenDeBomberos"] = new(@"\b(dictamen|reporte|constancia|acta|informe)\b[^.]{0,40}\b(bomberos|proteccion " +
                                     @"civil)\b|\b(bomberos|proteccion civil)\b[^.]{0,30}\b(dictamen|reporte|" +
                                     @"constancia|acta|informe)\b", RegexOptions.CultureInvariant),
        ["DenunciaAnteMinisterioPublico"] = new(@"\b(denuncia|carpeta de investigacion|ministerio publico|fiscalia)\b",
            RegexOptions.CultureInvariant),
        ["InformeMedico"] = new(@"\b(informe|parte|certificado|nota|reporte|dictamen) medico|\bdiagnostico\b|" +
                                @"\balta medica\b", RegexOptions.CultureInvariant),
        ["InventarioDeBienes"] = new(@"\binventario\b|\b(lista|relacion)\s+de\s+(los\s+|las\s+|lo\s+)?(bienes|objetos|" +
                                     @"articulos|cosas|muebles|robado|danado)", RegexOptions.CultureInvariant),
        ["DatosDelTercero"] = new(@"\b(sus datos|datos del (tercero|otro|vecino|conductor)|placas del|su telefono|" +
                                  @"su poliza|su aseguradora|se llama)\b|\[persona_\d+\]", RegexOptions.CultureInvariant),
    };

    private static readonly (string Nombre, Regex Patron)[] Senales =
    [
        ("SinTestigosNiEvidencia", new Regex(@"\b(nadie (vio|lo vio|se dio cuenta)|no hay testigos|sin testigos|" +
                                             @"no hubo testigos|ninguna camara|no hay camaras)\b",
            RegexOptions.CultureInvariant)),
        ("BienesSinComprobante", new Regex(@"\b(sin facturas?|no tengo (las )?facturas?|en efectivo|sin comprobantes?|" +
                                           @"no tengo (los )?comprobantes?|sin tickets?)\b",
            RegexOptions.CultureInvariant)),
        ("RelatoContradictorio", new Regex(@"\b(primero dije|en realidad|me equivoque|corrijo)\b",
            RegexOptions.CultureInvariant)),
        ("ContratacionReciente", new Regex(@"\bacabo de contratar\b|\b(contrate|contrato)\b[^.]{0,40}\b(hace (unos |pocos " +
                                           @")?(\w+ )?dias|la semana pasada|ayer)\b", RegexOptions.CultureInvariant)),
        ("PresionPorPagoRapido", new Regex(@"\b(lo antes posible|necesito (que me paguen|el dinero|el pago)|me urge|" +
                                           @"hoy mismo|de inmediato)\b", RegexOptions.CultureInvariant)),
    ];

    [GeneratedRegex(@"\b(no|sin|ni|todavia no|aun no|falta|faltan)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Negacion();
}

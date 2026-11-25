using System.Net;
using System.Text.Json;
using Perito.Asistente.Proveedores;
using Perito.Evaluacion;

namespace Perito.Asistente.Tests;

/// <summary>
/// La llave de Groq: se lee de un archivo con permisos 600 y no aparece NUNCA en la salida de la consola, el informe,
/// la caché de casos, las observaciones ni los errores. El servidor falso confirma que sí se usó para autorizar.
/// </summary>
public sealed class LlaveTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("perito-llave-").FullName;

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private string Archivo(string contenido, UnixFileMode permisos)
    {
        var ruta = Path.Join(_carpeta, "groq.env");
        File.WriteAllText(ruta, contenido);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ruta, permisos);
        }

        return ruta;
    }

    [Fact]
    public void Se_lee_de_un_archivo_600_y_nunca_se_imprime()
    {
        var ruta = Archivo($"# llave de Groq\nGROQ_API_KEY={GroqProtocoloTests.LlaveFalsa}\n",
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var llave = SecretoApi.LeerDeArchivo(ruta, "GROQ_API_KEY");
        var opciones = new Perito.Asistente.OpcionesDelAsistente { Proveedor = "groq", Llave = llave };

        Assert.Equal("[secreto]", llave.ToString());
        Assert.DoesNotContain(GroqProtocoloTests.LlaveFalsa, opciones.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(GroqProtocoloTests.LlaveFalsa, JsonSerializer.Serialize(opciones), StringComparison.Ordinal);
    }

    [Fact]
    public void La_guarda_reconoce_la_llave_en_un_texto_sin_revelarla()
    {
        var llave = SecretoApi.De(GroqProtocoloTests.LlaveFalsa);
        Assert.True(llave.ApareceEn("x" + GroqProtocoloTests.LlaveFalsa + "y"));
        Assert.False(llave.ApareceEn("informe sin llave"));
    }

    /// <summary>Permisos POSIX: el CI corre en Linux.</summary>
    [Fact]
    public void Un_archivo_que_otros_pueden_leer_se_rechaza_sin_mostrar_la_llave()
    {
        var ruta = Archivo($"GROQ_API_KEY={GroqProtocoloTests.LlaveFalsa}\n",
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var e = Assert.Throws<InvalidOperationException>(() => SecretoApi.LeerDeArchivo(ruta, "GROQ_API_KEY"));
        Assert.Contains("600", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(GroqProtocoloTests.LlaveFalsa, e.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("OTRA_LLAVE=abc\n")]
    [InlineData("GROQ_API_KEY=\n")]
    public void Sin_la_linea_con_valor_es_un_error_con_nombre(string contenido) =>
        Assert.Throws<InvalidOperationException>(() => SecretoApi.LeerDeArchivo(
            Archivo(contenido, UnixFileMode.UserRead | UnixFileMode.UserWrite), "GROQ_API_KEY"));

    /// <summary>La corrida por la CLI con un servidor falso: la llave autoriza, pero no queda en ninguna salida.</summary>
    [Fact]
    public async Task La_corrida_de_groq_no_deja_la_llave_en_consola_informe_ni_cache()
    {
        var llave = Archivo($"GROQ_API_KEY={GroqProtocoloTests.LlaveFalsa}\n", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var servidor = new OpenAIFalso();
        using var consola = new StringWriter();
        using var errores = new StringWriter();
        using var http = new HttpClient(servidor);
        var salida = Path.Join(_carpeta, "informe");
        var cache = Path.Join(_carpeta, "casos.jsonl");

        var codigo = await Program.EjecutarAsync(
        [
            "--proveedor", "groq", "--fecha", "2026-11-25", "--casos", "2", "--archivo-de-llave", llave,
            "--endpoint", "https://groq.prueba/openai/v1", "--salida", salida, "--cache", cache, "--rpm", "6000",
            "--tpm", "10000000", "--usd-entrada", "0.15", "--usd-salida", "0.60",
            "--eval", CorridaDelConjunto.Eval,
        ], consola, errores, http);

        Assert.Equal(0, codigo);
        Assert.Equal(12, servidor.Solicitudes.Count);                 // 2 casos × 3 contextos × 2 llamadas
        Assert.All(servidor.Autorizaciones, a => Assert.Equal("Bearer " + GroqProtocoloTests.LlaveFalsa, a));
        var todo = string.Join("\n", consola.ToString(), errores.ToString(), File.ReadAllText(cache),
            File.ReadAllText(Path.Join(salida, "groq.json")), File.ReadAllText(Path.Join(salida, "groq.md")));
        Assert.DoesNotContain(GroqProtocoloTests.LlaveFalsa, todo, StringComparison.Ordinal);
        Assert.Contains("openai/gpt-oss-120b", todo, StringComparison.Ordinal);
        Assert.Contains("contra la línea base determinista", File.ReadAllText(Path.Join(salida, "groq.md")),
            StringComparison.Ordinal);
    }

    /// <summary>Si un límite corta la corrida, el informe se arma con lo guardado, sin llamar al modelo, y dice que es
    /// PARCIAL con su N; la línea base se compara sobre los mismos casos.</summary>
    [Fact]
    public async Task El_informe_desde_la_cache_es_parcial_dice_su_N_y_no_llama_al_modelo()
    {
        var llave = Archivo($"GROQ_API_KEY={GroqProtocoloTests.LlaveFalsa}\n", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var servidor = new OpenAIFalso();
        using var http = new HttpClient(servidor);
        using var consola = new StringWriter();
        using var errores = new StringWriter();
        var salida = Path.Join(_carpeta, "informe");
        var cache = Path.Join(_carpeta, "casos.jsonl");
        string[] comunes = ["--proveedor", "groq", "--fecha", "2026-11-25", "--archivo-de-llave", llave,
            "--endpoint", "https://groq.prueba/openai/v1", "--salida", salida, "--cache", cache, "--rpm", "6000",
            "--tpm", "10000000", "--eval", CorridaDelConjunto.Eval];
        Assert.Equal(0, await Program.EjecutarAsync([.. comunes, "--casos", "2", "--contextos", "normal"], consola,
            errores, http));
        var llamadas = servidor.Solicitudes.Count;

        Assert.Equal(0, await Program.EjecutarAsync([.. comunes, "--solo-cache", "si"], consola, errores, http));

        Assert.Equal(llamadas, servidor.Solicitudes.Count);
        using var informe = JsonDocument.Parse(File.ReadAllText(Path.Join(salida, "groq.json")));
        Assert.True(informe.RootElement.GetProperty("parcial").GetBoolean());
        var n = informe.RootElement.GetProperty("corridas").EnumerateArray()
            .Select(c => c.GetProperty("metricas").GetProperty("casos").GetInt32()).ToList();
        Assert.Equal(new[] { 2, 0, 0 }, n);
        var md = File.ReadAllText(Path.Join(salida, "groq.md"));
        Assert.Contains("**PARCIAL.**", md, StringComparison.Ordinal);
        Assert.Contains("| Casos (N) | 2 | 2 |", md, StringComparison.Ordinal);    // LLM y base sobre los mismos 2
    }

    [Fact]
    public async Task Un_rechazo_del_proveedor_corta_la_corrida_con_su_estado_y_sin_la_llave()
    {
        var llave = Archivo($"GROQ_API_KEY={GroqProtocoloTests.LlaveFalsa}\n", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var servidor = new OpenAIFalso();
        servidor.Errores.Enqueue((HttpStatusCode.Unauthorized, null));
        using var errores = new StringWriter();
        using var consola = new StringWriter();
        using var http = new HttpClient(servidor);

        var codigo = await Program.EjecutarAsync(
        [
            "--proveedor", "groq", "--fecha", "2026-11-25", "--casos", "1", "--archivo-de-llave", llave,
            "--endpoint", "https://groq.prueba/openai/v1", "--salida", Path.Join(_carpeta, "informe"),
            "--cache", Path.Join(_carpeta, "casos.jsonl"), "--eval", CorridaDelConjunto.Eval,
        ], consola, errores, http);

        Assert.Equal(3, codigo);
        Assert.Contains("HTTP 401", errores.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(GroqProtocoloTests.LlaveFalsa, errores.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("límite de prueba", errores.ToString(), StringComparison.Ordinal);   // ni el cuerpo
    }

    [Fact]
    public async Task Sin_fecha_no_corre_un_llm_real()
    {
        using var errores = new StringWriter();
        using var consola = new StringWriter();
        var codigo = await Program.EjecutarAsync(["--proveedor", "ollama", "--eval", CorridaDelConjunto.Eval],
            consola, errores);
        Assert.Equal(2, codigo);
        Assert.Contains("--fecha", errores.ToString(), StringComparison.Ordinal);
    }
}

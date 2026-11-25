using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Perito.Api.Tests;

/// <summary>PostgreSQL real (Testcontainers, imagen fija por digest) y una API por prueba sobre su propia base.</summary>
public sealed class ApiBase : IAsyncLifetime
{
    public const string Imagen =
        "postgres:16-alpine@sha256:f2f98726820d0342055351c6e9d36e705f5bfffb3dd53ff556325114b08afb6d";

    public static readonly string LlaveJwt = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(7 * i))
        .ToArray());

    private static readonly string LlaveDeCifrado = Convert.ToBase64String(Enumerable.Range(0, 32)
        .Select(i => (byte)(3 * i + 1)).ToArray());

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder(Imagen)
        .WithDatabase("postgres")
        .WithCreateParameterModifier(p => p.HostConfig!.Memory = 512L << 20)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilInternalTcpPortIsAvailable(5432)
            .UntilCommandIsCompleted("psql", "-h", "127.0.0.1", "-U", "postgres", "-tAc", "SELECT 1"))
        .Build();

    public async Task<Api> NuevaApiAsync(bool conSalida = false, int propuestasPorMinuto = 50,
        IReadOnlyDictionary<string, string?>? cambios = null)
    {
        var nombre = "perito_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conexion = new NpgsqlConnection(_contenedor.GetConnectionString()))
        {
            await conexion.OpenAsync();
            await using var crear = new NpgsqlCommand($"CREATE DATABASE {nombre}", conexion);
            await crear.ExecuteNonQueryAsync();
        }

        var ajustes = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Perito"] = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString())
                { Database = nombre }.ConnectionString,
            ["Perito:MigrarAlArrancar"] = "true",
            ["Perito:Jwt:Llave"] = LlaveJwt,
            ["Perito:Cifrado:LlaveActiva"] = "k1",
            ["Perito:Cifrado:Llaves:k1"] = LlaveDeCifrado,
            ["Perito:Salida:Habilitada"] = conSalida ? "true" : "false",
            ["Perito:Salida:IntervaloSegundos"] = "1",
            ["Perito:LimiteDePropuestasPorMinuto"] = propuestasPorMinuto.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        foreach (var (llave, valor) in cambios ?? new Dictionary<string, string?>())
        {
            ajustes[llave] = valor;
        }

        return new Api(ajustes);
    }

    public Task InitializeAsync() => _contenedor.StartAsync();

    public Task DisposeAsync() => _contenedor.DisposeAsync().AsTask();
}

[CollectionDefinition(nameof(ApiBase))]
public sealed class ColeccionDeApi : ICollectionFixture<ApiBase>;

/// <summary>La API real (WebApplicationFactory) con sus ajustes y clientes con token por rol.</summary>
public sealed class Api : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly WebApplicationFactory<Program> _base = new();
    private readonly WebApplicationFactory<Program> _fabrica;

    public Api(IReadOnlyDictionary<string, string?> ajustes)
    {
        _fabrica = _base.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Pruebas");
            foreach (var (llave, valor) in ajustes)
            {
                b.UseSetting(llave, valor);
            }
        });
    }

    public IServiceProvider Servicios => _fabrica.Services;

    public HttpClient SinToken() => _fabrica.CreateClient();

    public HttpClient Como(string rol, string? usuario = null, string? llave = null, TimeSpan? vigencia = null)
    {
        var cliente = _fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            Token(rol, usuario ?? rol + ".uno", llave ?? ApiBase.LlaveJwt, vigencia ?? TimeSpan.FromMinutes(10)));
        return cliente;
    }

    public static string Token(string rol, string usuario, string llave, TimeSpan vigencia)
    {
        var ahora = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "perito-local",
            Audience = "perito-api",
            Subject = new ClaimsIdentity([new Claim("sub", usuario), new Claim("rol", rol)]),
            NotBefore = ahora.AddMinutes(-20),
            IssuedAt = ahora.AddMinutes(-20),
            Expires = ahora + vigencia,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(llave)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    public static async Task<Guid> IdCreado(HttpResponseMessage r)
    {
        Assert.Equal(System.Net.HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    /// <summary>Un titular, una póliza de auto amplia y un aviso: devuelve el siniestro.</summary>
    public async Task<(Guid Titular, string Folio, Guid Siniestro)> AvisoAsync(string folio = "AFV-AUT-000100")
    {
        var suscriptor = Como("suscriptor");
        var titular = await IdCreado(await suscriptor.PostAsJsonAsync("/api/asegurados", new
        {
            nombre = "Lucía Ferrer Montaño", rfc = "FEML850101AB1", correo = "lucia.ferrer@example.com",
            telefono = "55 4100 2233", clabe = "002180000000000012",
        }));
        // La API usa el reloj del sistema: la vigencia se arma alrededor de hoy para que la prueba no caduque.
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var poliza = await suscriptor.PostAsJsonAsync("/api/polizas", new
        {
            folio, ramo = "Auto", aseguradoId = titular, inicio = hoy.AddDays(-60).ToString("O"),
            fin = hoy.AddDays(300).ToString("O"), primaPagadaHasta = hoy.AddDays(300).ToString("O"),
            coberturas = new object[]
            {
                new { codigo = "DanosMateriales", sumaAsegurada = 350000, tipoDeDeducible = "PorcentajeSuma", deducible = 0.05, coaseguro = 0 },
                new { codigo = "Cristales", sumaAsegurada = 30000, tipoDeDeducible = "PorcentajePerdida", deducible = 0.2, coaseguro = 0 },
            },
        });
        Assert.Equal(System.Net.HttpStatusCode.Created, poliza.StatusCode);
        var siniestro = await IdCreado(await Como("operador").PostAsJsonAsync("/api/siniestros", new
        {
            folioPoliza = folio, fechaOcurrencia = hoy.AddDays(-1).ToString("O"),
            descripcion = "Soy Lucía Ferrer Montaño. Ayer un camión me alcanzó por detrás en la calle Olivo 24; " +
                          "la defensa y la cajuela quedaron hundidas. Tomé fotos de los daños.",
            estimacion = 100000,
        }));
        return (titular, folio, siniestro);
    }

    public async ValueTask DisposeAsync()
    {
        await _fabrica.DisposeAsync();
        await _base.DisposeAsync();
    }
}

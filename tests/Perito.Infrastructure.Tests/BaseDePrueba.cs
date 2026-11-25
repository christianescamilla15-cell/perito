using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Puertos;
using Perito.Application.Siniestros;
using Perito.Domain.Ajustadores;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.Infrastructure.Eventos;
using Perito.Infrastructure.Privacidad;
using Testcontainers.PostgreSql;

namespace Perito.Infrastructure.Tests;

/// <summary>
/// PostgreSQL REAL en un contenedor (Testcontainers), imagen fija por digest, con tope de memoria y espera explícita
/// (puerto y una consulta que responde, nunca un retraso fijo). Cada prueba pide su propia base, migrada.
/// </summary>
public sealed class BaseDePrueba : IAsyncLifetime
{
    public const string Imagen =
        "postgres:16-alpine@sha256:f2f98726820d0342055351c6e9d36e705f5bfffb3dd53ff556325114b08afb6d";

    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder(Imagen)
        .WithDatabase("postgres")
        .WithCreateParameterModifier(p => p.HostConfig!.Memory = 512L << 20)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilInternalTcpPortIsAvailable(5432)
            .UntilCommandIsCompleted("psql", "-h", "127.0.0.1", "-U", "postgres", "-tAc", "SELECT 1"))
        .Build();

    public static readonly OpcionesDeCifrado Cifrado = new("k1", new Dictionary<string, string>
    {
        ["k1"] = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray()),
    });

    public async Task<Banco> NuevoBancoAsync(params IConsumidorDeEventos[] consumidoresExtra)
    {
        var nombre = "perito_" + Guid.NewGuid().ToString("N")[..12];
        await using (var conexion = new NpgsqlConnection(_contenedor.GetConnectionString()))
        {
            await conexion.OpenAsync();
            await using var crear = new NpgsqlCommand($"CREATE DATABASE {nombre}", conexion);
            await crear.ExecuteNonQueryAsync();
        }

        var cadena = new NpgsqlConnectionStringBuilder(_contenedor.GetConnectionString()) { Database = nombre }
            .ConnectionString;
        var banco = new Banco(cadena, consumidoresExtra);
        await banco.Proveedor.AplicarMigracionesAsync();
        return banco;
    }

    public Task InitializeAsync() => _contenedor.StartAsync();

    public Task DisposeAsync() => _contenedor.DisposeAsync().AsTask();
}

[CollectionDefinition(nameof(BaseDePrueba))]
public sealed class ColeccionDeBase : ICollectionFixture<BaseDePrueba>;

/// <summary>Reloj de prueba que se puede mover.</summary>
public sealed class RelojDePrueba : IClock
{
    public DateTimeOffset Ahora { get; set; } = new(2026, 6, 15, 17, 0, 0, TimeSpan.Zero);
}

/// <summary>Asistente fijo: siempre propone Colisión con su cláusula.</summary>
public sealed class AsistenteFijo : IAsistenteDeTriaje
{
    public Task<PropuestaGenerada> ProponerAsync(SolicitudAlAsistente solicitud, CancellationToken ct) =>
        Task.FromResult(new PropuestaGenerada("fijo", "fijo-1", "Colision", "AUT-2.1-a", ["PresupuestoDeReparacion"],
            [], [], "fija", ["AUT-2.1-a"], [], 0));
}

/// <summary>La composición REAL de la infraestructura sobre una base propia, con reloj y asistente de prueba.</summary>
public sealed class Banco : IAsyncDisposable
{
    public Banco(string cadena, IEnumerable<IConsumidorDeEventos> consumidoresExtra)
    {
        Cadena = cadena;
        var servicios = new ServiceCollection().AddLogging();
        servicios.AgregarPerito(cadena, BaseDePrueba.Cifrado,
            new OpcionesDeSalida { EsperaBase = TimeSpan.FromSeconds(10), IntentosMaximos = 3 });
        servicios.AddSingleton<IClock>(Reloj);
        servicios.AddSingleton<IAsistenteDeTriaje, AsistenteFijo>();
        foreach (var consumidor in consumidoresExtra)
        {
            servicios.AddSingleton(consumidor);
        }

        Proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public string Cadena { get; }

    public RelojDePrueba Reloj { get; } = new();

    public ServiceProvider Proveedor { get; }

    public DespachadorDeSalida Despachador => Proveedor.GetRequiredService<DespachadorDeSalida>();

    public async Task<T> Enviar<T>(ISolicitud<T> solicitud)
    {
        await using var alcance = Proveedor.CreateAsyncScope();
        return await alcance.ServiceProvider.GetRequiredService<IMediador>().EnviarAsync(solicitud);
    }

    public async Task<(Guid Titular, string Folio)> PolizaAutoAsync(string? folio = null)
    {
        var titular = await Enviar(new RegistrarAsegurado("Lucía Ferrer Montaño", "FEML850101AB1",
            "lucia.ferrer@example.com", "55 4100 2233", "002180000000000012"));
        folio ??= "AFV-AUT-" + Guid.NewGuid().ToString("N")[..6];
        var r = await Enviar(new EmitirPoliza(folio, Ramo.Auto, titular.Valor, new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31),
            [
                new(CodigoCobertura.DanosMateriales, 350_000m, new Deducible(TipoDeDeducible.PorcentajeSuma, 0.05m), 0m),
                new(CodigoCobertura.RoboTotal, 350_000m, new Deducible(TipoDeDeducible.PorcentajeSuma, 0.10m), 0m),
            ]));
        Assert.True(r.EsExito, r.Error?.Mensaje);
        return (titular.Valor, folio);
    }

    public const string Descripcion =
        "Soy Lucía Ferrer Montaño; choqué contra un poste en la calzada de los Fresnos. Mi RFC es FEML850101AB1 y " +
        "mi teléfono 55 4100 2233.";

    public async Task<Guid> AvisoAsync(string folio, DateOnly? ocurrencia = null)
    {
        var r = await Enviar(new AvisarSiniestro(folio, ocurrencia ?? new DateOnly(2026, 6, 14), Descripcion, null,
            "Esguince cervical leve."));
        Assert.True(r.EsExito, r.Error?.Mensaje);
        return r.Valor;
    }

    /// <summary>Lleva un siniestro de colisión hasta reservado con documentos (reserva neta de 82,500).</summary>
    public async Task<Guid> ReservadoAsync(string folio, Guid ajustador, decimal estimacion = 100_000m)
    {
        var id = await AvisoAsync(folio);
        Exito(await Enviar(new TriarSiniestro(id, TipoDeSiniestro.Colision, null, "ajustador.uno")));
        Exito(await Enviar(new AsignarAjustador(id, ajustador)));
        Exito(await Enviar(new ConstituirReserva(id, estimacion)));
        foreach (var dato in Catalogo.De(TipoDeSiniestro.Colision).Datos)
        {
            Exito(await Enviar(new RegistrarDatoRecibido(id, dato)));
        }

        return id;
    }

    public async Task<Guid> AjustadorAsync() =>
        (await Enviar(new RegistrarAjustador("Ajustador de prueba", [Especialidad.Auto, Especialidad.Hogar]))).Valor;

    /// <summary>Despacha hasta vaciar la bandeja (lo listo para entregar).</summary>
    public async Task DespacharTodoAsync()
    {
        int tomados;
        do
        {
            tomados = await Despachador.ProcesarLoteAsync(CancellationToken.None);
        }
        while (tomados > 0);
    }

    public async Task<T?> EscalarAsync<T>(string sql, params object[] parametros)
    {
        await using var conexion = new NpgsqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conexion);
        foreach (var p in parametros)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = p });
        }

        var r = await cmd.ExecuteScalarAsync();
        return r is null or DBNull ? default : (T)r;
    }

    public async Task<List<string>> TextosAsync(string sql, params object[] parametros)
    {
        await using var conexion = new NpgsqlConnection(Cadena);
        await conexion.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conexion);
        foreach (var p in parametros)
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = p });
        }

        await using var lector = await cmd.ExecuteReaderAsync();
        var filas = new List<string>();
        while (await lector.ReadAsync())
        {
            filas.Add(lector.IsDBNull(0) ? string.Empty : lector.GetString(0));
        }

        return filas;
    }

    public static void Exito(Resultado r) => Assert.True(r.EsExito, r.Error?.Codigo + ": " + r.Error?.Mensaje);

    public ValueTask DisposeAsync() => Proveedor.DisposeAsync();
}

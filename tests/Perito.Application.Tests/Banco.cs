using Microsoft.Extensions.DependencyInjection;
using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Puertos;
using Perito.Application.Siniestros;
using Perito.Domain.Ajustadores;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.EnMemoria;

namespace Perito.Application.Tests;

/// <summary>Asistente de prueba con guion: devuelve lo que se le dé y guarda lo que recibió.</summary>
internal sealed class AsistenteGuionado : IAsistenteDeTriaje
{
    public List<SolicitudAlAsistente> Recibidas { get; } = [];

    public Func<SolicitudAlAsistente, PropuestaGenerada> Guion { get; set; } = _ => Generada("Colision", "AUT-2.1-a");

    public Task<PropuestaGenerada> ProponerAsync(SolicitudAlAsistente solicitud, CancellationToken ct)
    {
        Recibidas.Add(solicitud);
        return Task.FromResult(Guion(solicitud));
    }

    public static PropuestaGenerada Generada(string? tipo, string? clausula, string[]? datos = null,
        string[]? exclusiones = null, string[]? senales = null, string[]? recuperadas = null) =>
        new("guion", "guion-1", tipo, clausula, datos ?? [], exclusiones ?? [], senales ?? [], "justificación",
            recuperadas ?? ["AUT-2.1-a", "AUT-2.1-b", "AUT-2.3", "AUT-4.1", "GEN-7"], [], 0);
}

/// <summary>Un banco de pruebas: la composición en memoria con reloj fijo y el asistente con guion.</summary>
internal sealed class Banco
{
    public static readonly DateTimeOffset Inicio = new(2026, 6, 15, 17, 0, 0, TimeSpan.Zero);

    public Banco()
    {
        var servicios = new ServiceCollection().AgregarPeritoEnMemoria(Almacen, Reloj);
        servicios.AddSingleton<IAsistenteDeTriaje>(Asistente);
        Proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    public AlmacenEnMemoria Almacen { get; } = new();

    public RelojFijo Reloj { get; } = new(Inicio);

    public AsistenteGuionado Asistente { get; } = new();

    public ServiceProvider Proveedor { get; }

    /// <summary>Cada envío en su propio alcance, como una solicitud HTTP.</summary>
    public async Task<T> Enviar<T>(ISolicitud<T> solicitud)
    {
        await using var alcance = Proveedor.CreateAsyncScope();
        return await alcance.ServiceProvider.GetRequiredService<IMediador>().EnviarAsync(solicitud);
    }

    public async Task<Guid> Titular(string nombre = "Lucía Ferrer Montaño") =>
        (await Enviar(new RegistrarAsegurado(nombre, "FEML850101AB1", "lucia.ferrer@example.com", "55 4100 2233",
            "002180000000000012"))).Valor;

    public async Task<(Guid Titular, string Folio)> PolizaAuto(string folio = "AFV-AUT-000001",
        bool conDanosMateriales = true)
    {
        var titular = await Titular();
        var coberturas = new List<CoberturaContratada>
        {
            new(CodigoCobertura.RoboTotal, 350_000m, new Deducible(TipoDeDeducible.PorcentajeSuma, 0.10m), 0m),
            new(CodigoCobertura.ResponsabilidadCivilAuto, 1_500_000m, new Deducible(TipoDeDeducible.Fijo, 0m), 0m),
        };
        if (conDanosMateriales)
        {
            coberturas.Add(new(CodigoCobertura.DanosMateriales, 350_000m,
                new Deducible(TipoDeDeducible.PorcentajeSuma, 0.05m), 0m));
        }

        var r = await Enviar(new EmitirPoliza(folio, Ramo.Auto, titular, new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31), coberturas));
        Assert.True(r.EsExito, r.Error?.Mensaje);
        return (titular, folio);
    }

    public async Task<Guid> Aviso(string folio, DateOnly? ocurrencia = null, string? descripcion = null,
        string? lesiones = null)
    {
        var r = await Enviar(new AvisarSiniestro(folio, ocurrencia ?? new DateOnly(2026, 6, 14),
            descripcion ?? "Soy Lucía Ferrer Montaño; choqué contra un poste en la calzada de los Fresnos. " +
            "Mi teléfono es 55 4100 2233 y mi correo lucia.ferrer@example.com.", null, lesiones));
        Assert.True(r.EsExito, r.Error?.Mensaje);
        return r.Valor;
    }

    public async Task<Guid> Ajustador(params Especialidad[] especialidades) =>
        (await Enviar(new RegistrarAjustador("Ajustador de prueba", especialidades))).Valor;

    public static void Exito(Resultado r) => Assert.True(r.EsExito, r.Error?.Codigo + ": " + r.Error?.Mensaje);
}

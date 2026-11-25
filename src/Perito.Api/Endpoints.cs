using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Perito.Application.Abstracciones;
using Perito.Application.Lecturas;
using Perito.Application.Polizas;
using Perito.Application.Privacidad;
using Perito.Application.Siniestros;
using Perito.Application.Triaje;
using Perito.Domain.Ajustadores;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;

namespace Perito.Api;

/// <summary>Cuerpos de las solicitudes (sólo lo necesario: minimización).</summary>
public static class Cuerpos
{
    /// <summary>Alta de titular.</summary>
    public sealed record Titular(string Nombre, string Rfc, string Correo, string Telefono, string? Clabe);

    /// <summary>Una cobertura de la carátula.</summary>
    public sealed record Cobertura(CodigoCobertura Codigo, decimal SumaAsegurada, TipoDeDeducible TipoDeDeducible,
        decimal Deducible, decimal Coaseguro);

    /// <summary>Emisión de póliza.</summary>
    public sealed record Poliza(string Folio, Ramo Ramo, Guid AseguradoId, DateOnly Inicio, DateOnly Fin,
        DateOnly PrimaPagadaHasta, IReadOnlyList<Cobertura> Coberturas);

    /// <summary>Alta de ajustador.</summary>
    public sealed record Ajustador(string Nombre, IReadOnlyList<Especialidad> Especialidades);

    /// <summary>Aviso de siniestro.</summary>
    public sealed record Aviso(string FolioPoliza, DateOnly FechaOcurrencia, string Descripcion, decimal? Estimacion,
        string? DetalleDeLesiones);

    /// <summary>Triaje.</summary>
    public sealed record Triaje(TipoDeSiniestro Tipo, Guid? PropuestaId);

    /// <summary>Asignación.</summary>
    public sealed record Asignacion(Guid AjustadorId);

    /// <summary>Dato recibido.</summary>
    public sealed record Dato(DatoRequerido Nombre);

    /// <summary>Reserva inicial.</summary>
    public sealed record Reserva(decimal Estimacion);

    /// <summary>Ajuste de reserva.</summary>
    public sealed record Ajuste(decimal NuevoSaldo, string Motivo);

    /// <summary>Dictamen: los hechos verificados.</summary>
    public sealed record Dictamen(decimal PerdidaAjustada, IReadOnlyList<CodigoExclusion>? Exclusiones,
        ResultadoDeInvestigacion? Investigacion);

    /// <summary>Pago.</summary>
    public sealed record Pago(string Referencia);
}

/// <summary>Las rutas de la API. Cada una manda una solicitud al mediador y traduce el resultado al contrato.</summary>
public static class Endpoints
{
    /// <summary>Encabezado con la finalidad de una lectura de datos personales.</summary>
    public const string EncabezadoDeFinalidad = "X-Finalidad";

    /// <summary>Registra todas las rutas.</summary>
    public static void MapearPerito(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        api.MapPost("/asegurados", (Cuerpos.Titular c, IMediador m, CancellationToken ct) =>
                Creado(m.EnviarAsync(new RegistrarAsegurado(c.Nombre, c.Rfc, c.Correo, c.Telefono, c.Clabe), ct),
                    id => $"/api/asegurados/{id}"))
            .RequireAuthorization(Roles.Suscriptor);

        api.MapGet("/asegurados/{id:guid}", async (Guid id, [FromHeader(Name = EncabezadoDeFinalidad)] string? finalidad,
                ClaimsPrincipal u, IMediador m, CancellationToken ct) =>
                Respuesta(await m.EnviarAsync(new ConsultarAsegurado(id, u.Usuario(), u.Rol(), finalidad ?? ""), ct)))
            .RequireAuthorization("lectura-de-titulares");

        api.MapPost("/asegurados/{id:guid}/anonimizacion", async (Guid id, ClaimsPrincipal u, IMediador m,
                CancellationToken ct) =>
                Respuesta(await m.EnviarAsync(new AnonimizarAsegurado(id, u.Usuario(), u.Rol()), ct)))
            .RequireAuthorization(Roles.Privacidad);

        api.MapGet("/asegurados/{id:guid}/accesos", async (Guid id, IMediador m, CancellationToken ct) =>
                Results.Ok(await m.EnviarAsync(new ConsultarAccesos(id), ct)))
            .RequireAuthorization(Roles.Privacidad);

        api.MapPost("/polizas", (Cuerpos.Poliza c, IMediador m, CancellationToken ct) =>
                Creado(m.EnviarAsync(new EmitirPoliza(c.Folio, c.Ramo, c.AseguradoId, c.Inicio, c.Fin,
                    c.PrimaPagadaHasta, c.Coberturas.Select(x => new CoberturaContratada(x.Codigo, x.SumaAsegurada,
                        new Deducible(x.TipoDeDeducible, x.Deducible), x.Coaseguro)).ToList()), ct),
                    _ => $"/api/polizas/{Uri.EscapeDataString(c.Folio.Trim())}"))
            .RequireAuthorization(Roles.Suscriptor);

        api.MapGet("/polizas/{folio}", async (string folio, IMediador m, CancellationToken ct) =>
            await m.EnviarAsync(new ConsultarPoliza(folio), ct) is { } p
                ? Results.Ok(p)
                : Problema(new Error("no_encontrado", "No existe la póliza.")));

        api.MapPost("/ajustadores", (Cuerpos.Ajustador c, IMediador m, CancellationToken ct) =>
                Creado(m.EnviarAsync(new RegistrarAjustador(c.Nombre, c.Especialidades), ct), id => $"/api/ajustadores/{id}"))
            .RequireAuthorization(Roles.Supervisor);

        api.MapPost("/siniestros", (Cuerpos.Aviso c, IMediador m, CancellationToken ct) =>
                Creado(m.EnviarAsync(new AvisarSiniestro(c.FolioPoliza, c.FechaOcurrencia, c.Descripcion, c.Estimacion,
                    c.DetalleDeLesiones), ct), id => $"/api/siniestros/{id}"))
            .RequireAuthorization(Roles.Operador);

        api.MapGet("/siniestros/{id:guid}", async (Guid id, [FromHeader(Name = EncabezadoDeFinalidad)] string? finalidad,
                ClaimsPrincipal u, IMediador m, CancellationToken ct) =>
                Respuesta(await m.EnviarAsync(new ConsultarSiniestro(id, u.Usuario(), u.Rol(), finalidad ?? ""), ct)))
            .RequireAuthorization("lectura-de-siniestros");

        api.MapPost("/siniestros/{id:guid}/propuestas-de-triaje", async (Guid id, ClaimsPrincipal u, IMediador m,
                CancellationToken ct) =>
            {
                var r = await m.EnviarAsync(new ProponerTriaje(id, u.Usuario()), ct);
                return r.EsExito
                    ? Results.Created($"/api/propuestas-de-triaje/{r.Valor.Id}", r.Valor)
                    : Problema(r.Error!);
            })
            .RequireAuthorization(Roles.Ajustador)
            .RequireRateLimiting(Composicion.LimiteDePropuestas);

        api.MapGet("/propuestas-de-triaje/{id:guid}", async (Guid id, IMediador m, CancellationToken ct) =>
                await m.EnviarAsync(new ConsultarPropuesta(id), ct) is { } p
                    ? Results.Ok(p)
                    : Problema(new Error("no_encontrado", "No existe la propuesta.")))
            .RequireAuthorization(Roles.Ajustador);

        api.MapPost("/siniestros/{id:guid}/triaje", async (Guid id, Cuerpos.Triaje c, ClaimsPrincipal u, IMediador m,
                CancellationToken ct) =>
                Respuesta(await m.EnviarAsync(new TriarSiniestro(id, c.Tipo, c.PropuestaId, u.Usuario()), ct)))
            .RequireAuthorization(Roles.Ajustador);

        api.MapPost("/siniestros/{id:guid}/asignacion", async (Guid id, Cuerpos.Asignacion c, IMediador m,
                CancellationToken ct) => Respuesta(await m.EnviarAsync(new AsignarAjustador(id, c.AjustadorId), ct)))
            .RequireAuthorization(Roles.Supervisor);

        api.MapPost("/siniestros/{id:guid}/datos-recibidos", async (Guid id, Cuerpos.Dato c, IMediador m,
                CancellationToken ct) => Respuesta(await m.EnviarAsync(new RegistrarDatoRecibido(id, c.Nombre), ct)))
            .RequireAuthorization(Roles.Ajustador);

        api.MapPost("/siniestros/{id:guid}/reserva", async (Guid id, Cuerpos.Reserva c, IMediador m,
                CancellationToken ct) => Respuesta(await m.EnviarAsync(new ConstituirReserva(id, c.Estimacion), ct)))
            .RequireAuthorization(Roles.Ajustador);

        api.MapPost("/siniestros/{id:guid}/reserva/ajustes", async (Guid id, Cuerpos.Ajuste c, IMediador m,
                CancellationToken ct) =>
                Respuesta(await m.EnviarAsync(new AjustarReserva(id, c.NuevoSaldo, c.Motivo), ct)))
            .RequireAuthorization(Roles.Ajustador);

        api.MapPost("/siniestros/{id:guid}/dictamen", async (Guid id, Cuerpos.Dictamen c, ClaimsPrincipal u,
                IMediador m, CancellationToken ct) =>
                Respuesta(await m.EnviarAsync(new Dictaminar(id, c.PerdidaAjustada, c.Exclusiones ?? [],
                    c.Investigacion, u.Usuario()), ct)))
            .RequireAuthorization(Roles.Ajustador);

        api.MapPost("/siniestros/{id:guid}/pago", async (Guid id, Cuerpos.Pago c, ClaimsPrincipal u, IMediador m,
                CancellationToken ct) =>
                Respuesta(await m.EnviarAsync(new PagarSiniestro(id, c.Referencia, u.Usuario()), ct)))
            .RequireAuthorization(Roles.Tesoreria);

        api.MapPost("/siniestros/{id:guid}/rechazo", async (Guid id, IMediador m, CancellationToken ct) =>
                Respuesta(await m.EnviarAsync(new RechazarSiniestro(id), ct)))
            .RequireAuthorization(Roles.Supervisor);

        api.MapGet("/tablero/reservas", async (IMediador m, CancellationToken ct) =>
                Results.Ok(await m.EnviarAsync(new ConsultarTableroDeReservas(), ct)))
            .RequireAuthorization(Roles.Supervisor);
    }

    /// <summary>Contrato de errores: no encontrado → 404; finalidad → 400; folio repetido → 409; cualquier regla de
    /// negocio → 422. Siempre application/problem+json con «codigo», sin detalles internos.</summary>
    public static IResult Problema(Error error) => Results.Problem(
        statusCode: error.Codigo switch
        {
            "no_encontrado" => StatusCodes.Status404NotFound,
            "finalidad_invalida" => StatusCodes.Status400BadRequest,
            "folio_duplicado" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity,
        },
        title: error.Mensaje,
        extensions: new Dictionary<string, object?> { ["codigo"] = error.Codigo });

    private static IResult Respuesta(Resultado r) => r.EsExito ? Results.NoContent() : Problema(r.Error!);

    private static IResult Respuesta<T>(Resultado<T> r) => r.EsExito ? Results.Ok(r.Valor) : Problema(r.Error!);

    private static async Task<IResult> Creado(Task<Resultado<Guid>> tarea, Func<Guid, string> ubicacion)
    {
        var r = await tarea;
        return r.EsExito ? Results.Created(ubicacion(r.Valor), new { id = r.Valor }) : Problema(r.Error!);
    }
}

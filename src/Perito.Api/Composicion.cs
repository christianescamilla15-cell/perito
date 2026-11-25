using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Perito.Application.Puertos;
using Perito.Asistente;
using Perito.Asistente.Proveedores;
using Perito.Infrastructure;
using Perito.Infrastructure.Eventos;
using Perito.Infrastructure.Privacidad;

namespace Perito.Api;

/// <summary>Roles de la API (claim «rol» del token).</summary>
public static class Roles
{
    /// <summary>Recibe avisos de siniestro.</summary>
    public const string Operador = "operador";

    /// <summary>Tría, reserva, recaba datos y dictamina.</summary>
    public const string Ajustador = "ajustador";

    /// <summary>Asigna ajustadores, comunica rechazos y ve el tablero.</summary>
    public const string Supervisor = "supervisor";

    /// <summary>Paga.</summary>
    public const string Tesoreria = "tesoreria";

    /// <summary>Da de alta titulares y pólizas.</summary>
    public const string Suscriptor = "suscriptor";

    /// <summary>Atiende derechos del titular: accesos y anonimización.</summary>
    public const string Privacidad = "privacidad";
}

/// <summary>Composición de la API: infraestructura, asistente, JWT, políticas por rol y límite de propuestas.</summary>
public static class Composicion
{
    /// <summary>Política de límite para las propuestas del asistente (costosas con un LLM).</summary>
    public const string LimiteDePropuestas = "propuestas";

    /// <summary>Registra todo. Falla al arrancar si falta un secreto.</summary>
    public static WebApplicationBuilder AgregarServicios(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var conexion = Requerido(cfg.GetConnectionString("Perito"), "ConnectionStrings:Perito");
        var llaves = cfg.GetSection("Perito:Cifrado:Llaves").GetChildren()
            .ToDictionary(c => c.Key, c => c.Value ?? string.Empty, StringComparer.Ordinal);
        if (llaves.Count == 0)
        {
            throw new InvalidOperationException("Faltan las llaves de cifrado (Perito:Cifrado:Llaves:<id>).");
        }

        builder.Services.AgregarPerito(conexion,
            new OpcionesDeCifrado(cfg["Perito:Cifrado:LlaveActiva"] ?? "k1", llaves));
        builder.Services.AgregarAsistente(new OpcionesDelAsistente
        {
            Proveedor = cfg["Perito:Asistente:Proveedor"] ?? "extractivo",
            Modelo = cfg["Perito:Asistente:Modelo"] ?? "qwen2.5:1.5b",
            Endpoint = cfg["Perito:Asistente:Endpoint"] ?? "http://127.0.0.1:11434",
        });
        if (cfg.GetValue("Perito:Salida:Habilitada", true))
        {
            builder.Services.AddHostedService(p => new TrabajadorDeSalida(p.GetRequiredService<DespachadorDeSalida>(),
                TimeSpan.FromSeconds(cfg.GetValue("Perito:Salida:IntervaloSegundos", 2)),
                p.GetRequiredService<ILogger<TrabajadorDeSalida>>()));
        }

        var llaveJwt = Convert.FromBase64String(Requerido(cfg["Perito:Jwt:Llave"], "Perito:Jwt:Llave"));
        if (llaveJwt.Length < 32)
        {
            throw new InvalidOperationException("La llave de firma JWT debe tener al menos 256 bits.");
        }

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = cfg["Perito:Jwt:Emisor"],
                ValidAudience = cfg["Perito:Jwt:Audiencia"],
                IssuerSigningKey = new SymmetricSecurityKey(llaveJwt),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                NameClaimType = "sub",
                RoleClaimType = "rol",
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });
        var autorizacion = builder.Services.AddAuthorizationBuilder();
        foreach (var rol in new[] { Roles.Operador, Roles.Ajustador, Roles.Supervisor, Roles.Tesoreria, Roles.Suscriptor,
                     Roles.Privacidad })
        {
            autorizacion.AddPolicy(rol, p => p.RequireRole(rol));
        }

        autorizacion.AddPolicy("lectura-de-siniestros",
            p => p.RequireRole(Roles.Ajustador, Roles.Supervisor, Roles.Operador, Roles.Privacidad));
        autorizacion.AddPolicy("lectura-de-titulares", p => p.RequireRole(Roles.Ajustador, Roles.Privacidad));

        var limite = cfg.GetValue("Perito:LimiteDePropuestasPorMinuto", 20);
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(LimiteDePropuestas, contexto => RateLimitPartition.GetFixedWindowLimiter(
                contexto.User.FindFirstValue("sub") ?? "anonimo",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = limite, Window = TimeSpan.FromMinutes(1) }));
        });

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ManejadorDeConflictos>();
        builder.Services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        return builder;
    }

    private static string Requerido(string? valor, string nombre) =>
        string.IsNullOrWhiteSpace(valor)
            ? throw new InvalidOperationException($"Falta la configuración {nombre} (variable de entorno).")
            : valor;
}

/// <summary>Despacha la bandeja de salida en segundo plano: lote tras lote; si no hay nada, espera el intervalo.</summary>
public sealed class TrabajadorDeSalida(DespachadorDeSalida despachador, TimeSpan intervalo,
    ILogger<TrabajadorDeSalida> bitacora) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await despachador.ProcesarLoteAsync(stoppingToken) > 0)
                {
                    continue;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                bitacora.LogError(e, "Falló un lote de la bandeja de salida; se reintenta en {Intervalo}", intervalo);
            }

            try
            {
                await Task.Delay(intervalo, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}

/// <summary>Un conflicto de concurrencia es un 409 con el contrato de errores, no un 500.</summary>
public sealed class ManejadorDeConflictos(IProblemDetailsService problemas) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext contexto, Exception exception, CancellationToken ct)
    {
        if (exception is not ConflictoDeConcurrencia conflicto)
        {
            return false;
        }

        contexto.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            ProblemDetails = { Status = 409, Title = "Conflicto", Detail = conflicto.Message, Extensions = { ["codigo"] = "conflicto" } },
        });
    }
}

/// <summary>Quién llama, desde el token.</summary>
public static class Llamante
{
    /// <summary>El usuario (claim «sub»).</summary>
    public static string Usuario(this ClaimsPrincipal p) => p.FindFirstValue("sub") ?? "desconocido";

    /// <summary>El primer rol del token.</summary>
    public static string Rol(this ClaimsPrincipal p) => p.FindFirstValue("rol") ?? "desconocido";
}

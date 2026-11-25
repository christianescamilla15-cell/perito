using Perito.Api;
using Perito.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.AgregarServicios();

var app = builder.Build();
// Contrato de errores: todo error sale como application/problem+json, sin la excepción ni la conexión.
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/salud", () => Results.Text("ok")).AllowAnonymous();
app.MapearPerito();

if (app.Configuration.GetValue("Perito:MigrarAlArrancar", false))
{
    await app.Services.AplicarMigracionesAsync();
}

await app.RunAsync();

/// <summary>Punto de entrada visible para WebApplicationFactory en las pruebas.</summary>
public partial class Program;

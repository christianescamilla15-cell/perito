using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Perito.Application.Puertos;

namespace Perito.Api.Tests;

/// <summary>La API completa sobre PostgreSQL real: roles, contrato de errores, finalidad, flujo de punta a punta con
/// el asistente (modelo extractivo) y el trabajador de la bandeja de salida.</summary>
[Collection(nameof(ApiBase))]
public class ApiTests(ApiBase pg)
{
    private static async Task<JsonElement> Problema(HttpResponseMessage r, HttpStatusCode esperado)
    {
        Assert.Equal(esperado, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static HttpRequestMessage Lectura(string ruta, string? finalidad)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (finalidad is not null)
        {
            m.Headers.Add(Endpoints.EncabezadoDeFinalidad, finalidad);
        }

        return m;
    }

    [Fact]
    public async Task La_salud_responde_sin_token()
    {
        await using var api = await pg.NuevaApiAsync();
        Assert.Equal("ok", await api.SinToken().GetStringAsync("/salud"));
    }

    [Fact]
    public async Task Sin_token_firma_valida_o_vigencia_es_401_y_con_otro_rol_es_403()
    {
        await using var api = await pg.NuevaApiAsync();
        var aviso = new { folioPoliza = "X", fechaOcurrencia = "2026-01-01", descripcion = new string('x', 30) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.SinToken().PostAsJsonAsync("/api/siniestros", aviso)).StatusCode);
        var otraLlave = Convert.ToBase64String(new byte[32]);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Como("operador", llave: otraLlave).PostAsJsonAsync("/api/siniestros", aviso)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await api.Como("operador", vigencia: TimeSpan.FromMinutes(-5)).PostAsJsonAsync("/api/siniestros", aviso))
            .StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await api.Como("ajustador").PostAsJsonAsync("/api/siniestros", aviso)).StatusCode);
    }

    /// <summary>De punta a punta por HTTP: aviso → propuesta (asistente con SK) → triaje por la persona, que puede
    /// apartarse de la propuesta → asignación → reserva → datos → dictamen → pago, cada paso con su rol; quien
    /// dictamina no paga.</summary>
    [Fact]
    public async Task El_flujo_completo_por_HTTP_respeta_roles_y_la_separacion_de_funciones()
    {
        await using var api = await pg.NuevaApiAsync();
        var (_, _, id) = await api.AvisoAsync();
        var ajustador = api.Como("ajustador", "ajustador.uno");
        var supervisor = api.Como("supervisor");

        var propuesta = await (await ajustador.PostAsync($"/api/siniestros/{id}/propuestas-de-triaje", null))
            .Content.ReadFromJsonAsync<PropuestaDeTriaje>(Api.Json);
        // La línea base propone Vuelco para «me alcanzó por detrás» (el mismo error que S01 en eval/informe): la persona
        // decide Colisión y deja constancia de qué propuesta revisó. La propuesta informa; no manda.
        Assert.True(propuesta!.CitaSustentada);
        Assert.Equal(Perito.Domain.Producto.Ramo.Auto,
            Perito.Domain.Producto.Catalogo.De(propuesta.Tipo!.Value).Ramo);

        Assert.Equal(HttpStatusCode.NoContent, (await ajustador.PostAsJsonAsync($"/api/siniestros/{id}/triaje",
            new { tipo = "Colision", propuestaId = propuesta.Id })).StatusCode);
        var idAjustador = await Api.IdCreado(await supervisor.PostAsJsonAsync("/api/ajustadores",
            new { nombre = "Ajustador Uno", especialidades = new[] { "Auto" } }));
        Assert.Equal(HttpStatusCode.NoContent, (await supervisor.PostAsJsonAsync($"/api/siniestros/{id}/asignacion",
            new { ajustadorId = idAjustador })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ajustador.PostAsJsonAsync($"/api/siniestros/{id}/reserva",
            new { estimacion = 100000 })).StatusCode);
        foreach (var dato in new[] { "Lugar", "Fotografias", "PresupuestoDeReparacion" })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await ajustador.PostAsJsonAsync(
                $"/api/siniestros/{id}/datos-recibidos", new { nombre = dato })).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await ajustador.PostAsJsonAsync($"/api/siniestros/{id}/dictamen",
            new { perdidaAjustada = 60000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ajustador.PostAsJsonAsync($"/api/siniestros/{id}/pago",
            new { referencia = "SPEI-1" })).StatusCode);
        var mismoUsuario = await Problema(await api.Como("tesoreria", "ajustador.uno")
            .PostAsJsonAsync($"/api/siniestros/{id}/pago", new { referencia = "SPEI-1" }), HttpStatusCode.UnprocessableEntity);
        Assert.Equal("separacion_de_funciones", mismoUsuario.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await api.Como("tesoreria", "tesoreria.uno")
            .PostAsJsonAsync($"/api/siniestros/{id}/pago", new { referencia = "SPEI-1" })).StatusCode);

        var vista = await (await ajustador.SendAsync(Lectura($"/api/siniestros/{id}", "atencion_del_siniestro")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pagado", vista.GetProperty("estado").GetString());
        Assert.Equal(42500m, vista.GetProperty("pago").GetProperty("monto").GetDecimal());
        Assert.Equal(propuesta.Id, vista.GetProperty("propuestaAceptada").GetGuid());
    }

    [Fact]
    public async Task Pedir_una_propuesta_no_cambia_el_estado_del_siniestro()
    {
        await using var api = await pg.NuevaApiAsync();
        var (_, _, id) = await api.AvisoAsync();
        var ajustador = api.Como("ajustador");
        Assert.Equal(HttpStatusCode.Created, (await ajustador.PostAsync($"/api/siniestros/{id}/propuestas-de-triaje", null))
            .StatusCode);
        var vista = await (await ajustador.SendAsync(Lectura($"/api/siniestros/{id}", "atencion_del_siniestro")))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Avisado", vista.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Null, vista.GetProperty("tipo").ValueKind);
    }

    [Fact]
    public async Task Leer_datos_personales_exige_finalidad_y_queda_en_el_registro_del_titular()
    {
        await using var api = await pg.NuevaApiAsync();
        var (titular, _, id) = await api.AvisoAsync();
        var ajustador = api.Como("ajustador", "ajustador.dos");

        var sin = await Problema(await ajustador.SendAsync(Lectura($"/api/siniestros/{id}", null)),
            HttpStatusCode.BadRequest);
        Assert.Equal("finalidad_invalida", sin.GetProperty("codigo").GetString());
        Assert.Equal(HttpStatusCode.OK,
            (await ajustador.SendAsync(Lectura($"/api/asegurados/{titular}", "atencion_del_siniestro"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await api.Como("operador").SendAsync(Lectura($"/api/asegurados/{titular}", "auditoria"))).StatusCode);

        var accesos = await api.Como("privacidad").GetFromJsonAsync<JsonElement>($"/api/asegurados/{titular}/accesos");
        var registro = Assert.Single(accesos.EnumerateArray());
        Assert.Equal(("ajustador.dos", "asegurado", "atencion_del_siniestro"), (registro.GetProperty("usuario").GetString(),
            registro.GetProperty("recurso").GetString(), registro.GetProperty("finalidad").GetString()));
    }

    [Fact]
    public async Task El_contrato_de_errores_es_problem_json_con_codigo_y_sin_detalles_internos()
    {
        await using var api = await pg.NuevaApiAsync();
        var (titular, _, id) = await api.AvisoAsync();

        var noExiste = await Problema(await api.Como("ajustador").SendAsync(
            Lectura($"/api/siniestros/{Guid.NewGuid()}", "auditoria")), HttpStatusCode.NotFound);
        Assert.Equal("no_encontrado", noExiste.GetProperty("codigo").GetString());

        var transicion = await Problema(await api.Como("tesoreria").PostAsJsonAsync($"/api/siniestros/{id}/pago",
            new { referencia = "SPEI-9" }), HttpStatusCode.UnprocessableEntity);
        Assert.Equal("transicion_invalida", transicion.GetProperty("codigo").GetString());

        var repetido = await Problema(await api.Como("suscriptor").PostAsJsonAsync("/api/polizas", new
        {
            folio = "AFV-AUT-000100", ramo = "Auto", aseguradoId = titular, inicio = "2026-01-01", fin = "2026-12-31",
            primaPagadaHasta = "2026-12-31", coberturas = Array.Empty<object>(),
        }), HttpStatusCode.Conflict);
        Assert.Equal("folio_duplicado", repetido.GetProperty("codigo").GetString());

        var abierto = await Problema(await api.Como("privacidad").PostAsync($"/api/asegurados/{titular}/anonimizacion", null),
            HttpStatusCode.UnprocessableEntity);
        Assert.Equal("siniestros_abiertos", abierto.GetProperty("codigo").GetString());

        var enumInvalido = await api.Como("ajustador").PostAsJsonAsync($"/api/siniestros/{id}/triaje",
            new { tipo = "Terremoto" });
        Assert.Equal(HttpStatusCode.BadRequest, enumInvalido.StatusCode);
        var cuerpo = await enumInvalido.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Exception", cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=", cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_conflicto_de_concurrencia_sale_como_409_con_su_codigo()
    {
        await using var api = await pg.NuevaApiAsync();
        var contexto = new DefaultHttpContext { RequestServices = api.Servicios };
        contexto.Response.Body = new MemoryStream();
        var manejador = ActivatorUtilities.CreateInstance<ManejadorDeConflictos>(api.Servicios);

        Assert.True(await manejador.TryHandleAsync(contexto, new ConflictoDeConcurrencia("otro guardó primero"), default));
        Assert.False(await manejador.TryHandleAsync(new DefaultHttpContext { RequestServices = api.Servicios },
            new InvalidOperationException("otra cosa"), default));
        Assert.Equal(StatusCodes.Status409Conflict, contexto.Response.StatusCode);
        contexto.Response.Body.Position = 0;
        var cuerpo = await JsonDocument.ParseAsync(contexto.Response.Body);
        Assert.Equal("conflicto", cuerpo.RootElement.GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Las_propuestas_tienen_limite_por_usuario_y_minuto()
    {
        await using var api = await pg.NuevaApiAsync(propuestasPorMinuto: 2);
        var (_, _, id) = await api.AvisoAsync();
        var ajustador = api.Como("ajustador", "ajustador.limite");
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            codigos.Add((await ajustador.PostAsync($"/api/siniestros/{id}/propuestas-de-triaje", null)).StatusCode);
        }

        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests }, codigos);
        Assert.Equal(HttpStatusCode.Created, (await api.Como("ajustador", "otro.ajustador")
            .PostAsync($"/api/siniestros/{id}/propuestas-de-triaje", null)).StatusCode);
    }

    /// <summary>El trabajador en segundo plano despacha la bandeja: se espera la condición (con tope), no un tiempo fijo.</summary>
    [Fact]
    public async Task El_trabajador_de_la_bandeja_lleva_la_reserva_al_tablero()
    {
        await using var api = await pg.NuevaApiAsync(conSalida: true);
        var (_, _, id) = await api.AvisoAsync();
        var ajustador = api.Como("ajustador");
        var supervisor = api.Como("supervisor");
        await ajustador.PostAsJsonAsync($"/api/siniestros/{id}/triaje", new { tipo = "Colision" });
        var idAjustador = await Api.IdCreado(await supervisor.PostAsJsonAsync("/api/ajustadores",
            new { nombre = "Ajustador Dos", especialidades = new[] { "Auto" } }));
        await supervisor.PostAsJsonAsync($"/api/siniestros/{id}/asignacion", new { ajustadorId = idAjustador });
        Assert.Equal(HttpStatusCode.NoContent, (await ajustador.PostAsJsonAsync($"/api/siniestros/{id}/reserva",
            new { estimacion = 100000 })).StatusCode);

        var limite = DateTime.UtcNow.AddSeconds(30);
        JsonElement tablero;
        do
        {
            tablero = await supervisor.GetFromJsonAsync<JsonElement>("/api/tablero/reservas");
            if (tablero.GetArrayLength() > 0)
            {
                break;
            }

            await Task.Delay(200);
        }
        while (DateTime.UtcNow < limite);

        var saldo = Assert.Single(tablero.EnumerateArray());
        Assert.Equal(("Auto", 82500m), (saldo.GetProperty("ramo").GetString(), saldo.GetProperty("reservaVigente").GetDecimal()));
    }

    [Theory]
    [InlineData("Perito:Jwt:Llave")]
    [InlineData("Perito:Cifrado:Llaves:k1")]
    [InlineData("ConnectionStrings:Perito")]
    public async Task Sin_un_secreto_la_API_no_arranca(string faltante)
    {
        await using var api = await pg.NuevaApiAsync(cambios: new Dictionary<string, string?> { [faltante] = "" });
        var e = Assert.ThrowsAny<Exception>(() => api.SinToken());
        Assert.Contains("Perito", e.ToString(), StringComparison.Ordinal);
    }
}

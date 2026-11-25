using Perito.Application.Abstracciones;
using Perito.Application.Polizas;
using Perito.Application.Puertos;
using Perito.Domain.Comun;
using Perito.Domain.Polizas;
using Perito.Domain.Privacidad;
using Perito.Domain.Producto;
using Perito.Domain.Siniestros;

namespace Perito.Application.Triaje;

/// <summary>Pide al asistente una propuesta de triaje. Guarda la propuesta; NO toca el siniestro.</summary>
public sealed record ProponerTriaje(Guid SiniestroId, string Usuario) : IComando<Resultado<PropuestaDeTriaje>>;

/// <summary>Consulta una propuesta guardada.</summary>
public sealed record ConsultarPropuesta(Guid PropuestaId) : IConsulta<PropuestaDeTriaje?>;

/// <summary>
/// Manejador de la propuesta. El texto sale seudonimizado con los identificadores del titular; el detalle de lesiones
/// (dato de salud) no se envía nunca. La respuesta del modelo pasa por <see cref="ValidadorDePropuesta"/> antes de
/// guardarse.
/// </summary>
public sealed class ManejadoresDeTriaje(
    ISiniestros siniestros, IPolizas polizas, IAsegurados asegurados, IPropuestasDeTriaje propuestas,
    IAsistenteDeTriaje asistente, IUnidadDeTrabajo unidad, IClock reloj)
    : IManejador<ProponerTriaje, Resultado<PropuestaDeTriaje>>, IManejador<ConsultarPropuesta, PropuestaDeTriaje?>
{
    /// <inheritdoc />
    public async Task<Resultado<PropuestaDeTriaje>> ManejarAsync(ProponerTriaje s, CancellationToken ct)
    {
        if (await siniestros.ObtenerAsync(s.SiniestroId, ct) is not { } siniestro)
        {
            return Resultado<PropuestaDeTriaje>.Fallo(Errores.NoEncontrado("el siniestro"));
        }

        if (siniestro.Estado != EstadoSiniestro.Avisado)
        {
            return Resultado<PropuestaDeTriaje>.Fallo("ya_triado",
                "La propuesta sirve para triar: el siniestro ya no está avisado.");
        }

        if (siniestro.Descripcion is null)
        {
            return Resultado<PropuestaDeTriaje>.Fallo("sin_descripcion", "El siniestro no tiene descripción.");
        }

        var poliza = await polizas.ObtenerAsync(siniestro.PolizaId, ct)
            ?? throw new InvalidOperationException("Siniestro sin póliza.");
        var titular = await asegurados.ObtenerAsync(siniestro.AseguradoId, ct);
        var texto = Seudonimizador.Seudonimizar(siniestro.Descripcion, titular?.Identificadores() ?? []);

        var generada = await asistente.ProponerAsync(new SolicitudAlAsistente(siniestro.Id, siniestro.Ramo, texto), ct);
        var propuesta = ValidadorDePropuesta.Validar(generada, siniestro.Id, siniestro.Ramo, poliza, Guid.NewGuid(),
            reloj.Ahora, s.Usuario);
        propuestas.Agregar(propuesta);
        await unidad.ConfirmarAsync(ct);
        return Resultado<PropuestaDeTriaje>.Exito(propuesta);
    }

    /// <inheritdoc />
    public Task<PropuestaDeTriaje?> ManejarAsync(ConsultarPropuesta s, CancellationToken ct) =>
        propuestas.ObtenerAsync(s.PropuestaId, ct);
}

/// <summary>
/// Guardas sobre lo que dijo el modelo: sólo sobreviven valores del catálogo del ramo; la cita sólo si está entre las
/// cláusulas RECUPERADAS (lo que el modelo vio) y ampara el tipo propuesto; la cobertura y si está contratada las
/// calcula la aplicación con la póliza. Lo descartado queda en las observaciones.
/// </summary>
public static class ValidadorDePropuesta
{
    /// <summary>Valida una propuesta generada.</summary>
    public static PropuestaDeTriaje Validar(PropuestaGenerada g, Guid siniestroId, Ramo ramo, Poliza poliza, Guid id,
        DateTimeOffset ahora, string usuario)
    {
        var observaciones = g.Observaciones.ToList();
        var tipo = Tipo(g.Tipo, ramo, observaciones);

        string? clausula = null;
        if (g.Clausula is { } cita)
        {
            var definicion = Catalogo.TipoPorClausula(cita);
            if (!g.ClausulasRecuperadas.Contains(cita, StringComparer.Ordinal))
            {
                observaciones.Add($"cita descartada: {cita} no está entre las cláusulas recuperadas");
            }
            else if (definicion is null || definicion.Ramo != ramo)
            {
                observaciones.Add($"cita descartada: {cita} no es una cláusula de cobertura del ramo {ramo}");
            }
            else if (tipo is not null && definicion.Tipo != tipo)
            {
                observaciones.Add($"cita descartada: {cita} ampara {definicion.Tipo}, no {tipo}");
            }
            else
            {
                clausula = cita;
            }
        }
        else
        {
            observaciones.Add("el modelo no citó una cláusula");
        }

        var requeridos = tipo is { } t ? Catalogo.De(t).Datos.ToHashSet() : null;
        var datos = Nombres<DatoRequerido>(g.DatosFaltantes, "dato", observaciones)
            .Where(d => requeridos is null || requeridos.Contains(d)).Distinct().Order().ToList();

        var exclusiones = new List<string>();
        foreach (var x in g.Exclusiones.Distinct())
        {
            if (Catalogo.ExclusionPorClausula(x) is { } e && e.Ramo == ramo &&
                g.ClausulasRecuperadas.Contains(x, StringComparer.Ordinal))
            {
                exclusiones.Add(x);
            }
            else
            {
                observaciones.Add($"exclusión descartada: {x}");
            }
        }

        var senales = Nombres<SenalDeRiesgo>(g.Senales, "señal", observaciones).Distinct().Order().ToList();
        CodigoCobertura? cobertura = tipo is { } tt ? Catalogo.De(tt).Cobertura : null;
        bool? contratada = cobertura is { } c ? poliza.Cobertura(c) is not null : null;
        var justificacion = g.Justificacion.Length > 1500 ? g.Justificacion[..1500] : g.Justificacion;

        return new PropuestaDeTriaje(id, siniestroId, ahora, usuario, g.Proveedor, g.Modelo, tipo, cobertura, clausula,
            clausula is not null, contratada, datos, exclusiones.Order(StringComparer.Ordinal).ToList(), senales,
            justificacion, g.ClausulasRecuperadas, observaciones, g.LatenciaMs);
    }

    private static TipoDeSiniestro? Tipo(string? valor, Ramo ramo, List<string> observaciones)
    {
        if (valor is null)
        {
            observaciones.Add("el modelo no propuso tipo");
            return null;
        }

        // Sólo nombres exactos: Enum.TryParse aceptaría «3» o «Colision, Vuelco».
        if (Enum.GetNames<TipoDeSiniestro>().Contains(valor, StringComparer.Ordinal))
        {
            var tipo = Enum.Parse<TipoDeSiniestro>(valor);
            if (Catalogo.De(tipo).Ramo == ramo)
            {
                return tipo;
            }
        }

        observaciones.Add($"tipo descartado: «{valor}» no es un tipo del ramo {ramo}");
        return null;
    }

    private static IEnumerable<T> Nombres<T>(IEnumerable<string> valores, string que, List<string> observaciones)
        where T : struct, Enum
    {
        foreach (var v in valores)
        {
            if (Enum.GetNames<T>().Contains(v, StringComparer.Ordinal))
            {
                yield return Enum.Parse<T>(v);
            }
            else
            {
                observaciones.Add($"{que} descartado: «{v}»");
            }
        }
    }
}

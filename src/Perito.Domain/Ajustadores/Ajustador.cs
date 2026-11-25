using Perito.Domain.Comun;
using Perito.Domain.Producto;

namespace Perito.Domain.Ajustadores;

/// <summary>Especialidad de un ajustador.</summary>
public enum Especialidad
{
    /// <summary>Siniestros de auto.</summary>
    Auto,

    /// <summary>Siniestros de hogar.</summary>
    Hogar,

    /// <summary>Investigación especial (GEN-9).</summary>
    Investigacion,
}

/// <summary>Ajustador: quien verifica el siniestro. La asignación la decide el dominio, no el asistente.</summary>
public sealed class Ajustador : RaizDeAgregado
{
    /// <summary>Casos abiertos que un ajustador puede llevar a la vez.</summary>
    public const int CargaMaxima = 10;

    private Ajustador(Guid id, string nombre, IEnumerable<Especialidad> especialidades, bool activo)
    {
        Id = id;
        Nombre = nombre;
        Especialidades = especialidades.ToHashSet();
        Activo = activo;
    }

    /// <summary>Nombre.</summary>
    public string Nombre { get; }

    /// <summary>Especialidades.</summary>
    public IReadOnlySet<Especialidad> Especialidades { get; }

    /// <summary>Si puede recibir casos.</summary>
    public bool Activo { get; private set; }

    /// <summary>Da de alta un ajustador.</summary>
    public static Resultado<Ajustador> Registrar(Guid id, string nombre, IReadOnlyCollection<Especialidad> especialidades)
    {
        nombre = (nombre ?? string.Empty).Trim();
        if (nombre.Length is < 3 or > 120)
        {
            return Resultado<Ajustador>.Fallo("nombre_invalido", "El nombre tiene de 3 a 120 caracteres.");
        }

        return especialidades.Count == 0
            ? Resultado<Ajustador>.Fallo("sin_especialidad", "El ajustador necesita al menos una especialidad.")
            : Resultado<Ajustador>.Exito(new Ajustador(id, nombre, especialidades, true));
    }

    /// <summary>Reconstruye un ajustador guardado.</summary>
    public static Ajustador Rehidratar(Guid id, string nombre, IEnumerable<Especialidad> especialidades, bool activo) =>
        new(id, nombre, especialidades, activo);

    /// <summary>Deja de recibir casos.</summary>
    public void Desactivar() => Activo = false;

    /// <summary>
    /// Si puede atender un siniestro: activo, con la especialidad del ramo (o de investigación si el siniestro la
    /// requiere) y con menos de <see cref="CargaMaxima"/> casos abiertos.
    /// </summary>
    public Resultado PuedeAtender(Ramo ramo, bool requiereInvestigacion, int casosAbiertos)
    {
        if (!Activo)
        {
            return Resultado.Fallo("ajustador_inactivo", "El ajustador no está activo.");
        }

        var necesaria = requiereInvestigacion
            ? Especialidad.Investigacion
            : ramo == Ramo.Auto ? Especialidad.Auto : Especialidad.Hogar;
        if (!Especialidades.Contains(necesaria))
        {
            return Resultado.Fallo("especialidad_incorrecta", $"El siniestro requiere un ajustador de {necesaria}.");
        }

        return casosAbiertos >= CargaMaxima
            ? Resultado.Fallo("carga_completa", $"El ajustador ya lleva {casosAbiertos} casos abiertos.")
            : Resultado.Exito();
    }
}

using Microsoft.EntityFrameworkCore;
using Perito.Application.Puertos;
using Perito.Infrastructure.Persistencia;

namespace Perito.Infrastructure.Privacidad;

/// <summary>Registro de accesos en su propio contexto: se escribe de inmediato, fuera de la unidad de trabajo de la
/// solicitud, y si falla la lectura de datos personales no se completa.</summary>
public sealed class RegistroDeAccesosEf(IDbContextFactory<PeritoDbContext> fabrica) : IRegistroDeAccesos
{
    /// <inheritdoc />
    public async Task RegistrarAsync(AccesoADatosPersonales a, CancellationToken ct)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        db.Accesos.Add(new AccesoFila
        {
            Id = a.Id, Fecha = a.Fecha, Usuario = a.Usuario, Rol = a.Rol, Recurso = a.Recurso, RecursoId = a.RecursoId,
            TitularId = a.TitularId, Finalidad = a.Finalidad,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccesoADatosPersonales>> DelTitularAsync(Guid titularId, CancellationToken ct)
    {
        await using var db = await fabrica.CreateDbContextAsync(ct);
        return await db.Accesos.AsNoTracking().Where(a => a.TitularId == titularId)
            .OrderBy(a => a.Fecha).ThenBy(a => a.Id)
            .Select(a => new AccesoADatosPersonales(a.Id, a.Fecha, a.Usuario, a.Rol, a.Recurso, a.RecursoId,
                a.TitularId, a.Finalidad))
            .ToListAsync(ct);
    }
}

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Perito.Infrastructure.Persistencia;

/// <summary>Esquema «perito» en PostgreSQL con nombres en snake_case, montos numeric(14,2) y control de concurrencia
/// optimista por xmin.</summary>
public sealed class PeritoDbContext(DbContextOptions<PeritoDbContext> opciones) : DbContext(opciones)
{
    /// <summary>Esquema de la base.</summary>
    public const string Esquema = "perito";

    internal DbSet<AseguradoFila> Asegurados => Set<AseguradoFila>();

    internal DbSet<PolizaFila> Polizas => Set<PolizaFila>();

    internal DbSet<AjustadorFila> Ajustadores => Set<AjustadorFila>();

    internal DbSet<SiniestroFila> Siniestros => Set<SiniestroFila>();

    internal DbSet<PropuestaFila> Propuestas => Set<PropuestaFila>();

    internal DbSet<MensajeDeSalida> Salida => Set<MensajeDeSalida>();

    internal DbSet<MensajeRecibido> Recibidos => Set<MensajeRecibido>();

    internal DbSet<SaldoDeReservaFila> Saldos => Set<SaldoDeReservaFila>();

    internal DbSet<AccesoFila> Accesos => Set<AccesoFila>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);

        modelBuilder.Entity<AseguradoFila>(e =>
        {
            e.ToTable("asegurados");
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<PolizaFila>(e =>
        {
            e.ToTable("polizas");
            e.Property(x => x.Folio).HasMaxLength(40);
            e.HasIndex(x => x.Folio).IsUnique();
            e.Property(x => x.Ramo).HasMaxLength(10);
            e.HasOne<AseguradoFila>().WithMany().HasForeignKey(x => x.AseguradoId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Coberturas).WithOne().HasForeignKey(x => x.PolizaId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<CoberturaFila>(e =>
        {
            e.ToTable("coberturas");
            e.HasKey(x => new { x.PolizaId, x.Codigo });
            e.Property(x => x.Codigo).HasMaxLength(40);
            e.Property(x => x.TipoDeDeducible).HasMaxLength(20);
            e.Property(x => x.SumaAsegurada).HasPrecision(14, 2);
            e.Property(x => x.ValorDeDeducible).HasPrecision(14, 4);
            e.Property(x => x.Coaseguro).HasPrecision(5, 4);
        });

        modelBuilder.Entity<AjustadorFila>(e =>
        {
            e.ToTable("ajustadores");
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<SiniestroFila>(e =>
        {
            e.ToTable("siniestros");
            e.HasOne<PolizaFila>().WithMany().HasForeignKey(x => x.PolizaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AjustadorFila>().WithMany().HasForeignKey(x => x.AjustadorId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PolizaId, x.FechaOcurrencia });
            e.HasIndex(x => x.AseguradoId);
            e.HasIndex(x => new { x.AjustadorId, x.Estado });
            foreach (var monto in new[] { "Estimacion", "PerdidaAjustada", "Indemnizacion", "PagoMonto" })
            {
                e.Property(monto).HasPrecision(14, 2);
            }

            e.HasMany(x => x.Movimientos).WithOne().HasForeignKey(x => x.SiniestroId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Version).IsRowVersion();
        });

        modelBuilder.Entity<MovimientoDeReservaFila>(e =>
        {
            e.ToTable("movimientos_de_reserva");
            e.HasKey(x => new { x.SiniestroId, x.Secuencia });
            e.Property(x => x.Monto).HasPrecision(14, 2);
            e.Property(x => x.Saldo).HasPrecision(14, 2);
        });

        modelBuilder.Entity<PropuestaFila>(e =>
        {
            e.ToTable("propuestas_de_triaje");
            e.Property(x => x.Documento).HasColumnType("jsonb");
            e.HasIndex(x => x.SiniestroId);
        });

        modelBuilder.Entity<MensajeDeSalida>(e =>
        {
            e.ToTable("mensajes_de_salida");
            e.Property(x => x.Contenido).HasColumnType("jsonb");
            e.Property(x => x.Tipo).HasMaxLength(80);
            e.Property(x => x.Secuencia).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.OcurridoEn, x.Secuencia }).HasFilter("procesado_en IS NULL AND NOT muerto");
        });

        modelBuilder.Entity<MensajeRecibido>(e =>
        {
            e.ToTable("mensajes_recibidos");
            e.HasKey(x => new { x.Consumidor, x.MensajeId });
            e.Property(x => x.Consumidor).HasMaxLength(80);
        });

        modelBuilder.Entity<SaldoDeReservaFila>(e =>
        {
            e.ToTable("saldos_de_reserva");
            e.HasKey(x => x.Ramo);
            e.Property(x => x.ReservaVigente).HasPrecision(16, 2);
        });

        modelBuilder.Entity<AccesoFila>(e =>
        {
            e.ToTable("registro_de_accesos");
            e.HasIndex(x => new { x.TitularId, x.Fecha });
        });

        // snake_case en todas las columnas (las tablas ya llevan su nombre); xmin la nombra Npgsql.
        foreach (var entidad in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var propiedad in entidad.GetProperties().Where(p => p.Name != "Version"))
            {
                propiedad.SetColumnName(Serpiente(propiedad.Name));
            }
        }
    }

    internal static string Serpiente(string nombre)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < nombre.Length; i++)
        {
            if (char.IsUpper(nombre[i]) && i > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(nombre[i]));
        }

        return sb.ToString();
    }
}

/// <summary>Sólo para `dotnet ef migrations add`: no se conecta.</summary>
public sealed class FabricaDeDiseno : IDesignTimeDbContextFactory<PeritoDbContext>
{
    /// <inheritdoc />
    public PeritoDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PeritoDbContext>()
            .UseNpgsql("Host=localhost;Database=perito_diseno",
                n => n.MigrationsHistoryTable("__historial_migraciones", PeritoDbContext.Esquema))
            .Options);
}

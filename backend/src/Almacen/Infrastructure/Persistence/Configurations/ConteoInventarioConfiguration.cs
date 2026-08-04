using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conteos;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

public sealed class ConteoInventarioConfiguration : IEntityTypeConfiguration<ConteoInventario>
{
    public void Configure(EntityTypeBuilder<ConteoInventario> builder)
    {
        builder.ToTable("conteos_inventario", t =>
        {
            t.HasCheckConstraint("ck_conteos_estado", "estado BETWEEN 0 AND 5");
            t.HasCheckConstraint("ck_conteos_tipo", "tipo BETWEEN 0 AND 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EmpresaId).IsRequired();
        builder.Property(x => x.Tipo).HasConversion<short>().IsRequired();
        builder.Property(x => x.Estado).HasConversion<short>().IsRequired();
        builder.Property(x => x.SubAlmacenId);
        builder.Property(x => x.FiltroFamilia).HasMaxLength(50);
        builder.Property(x => x.FechaPlanificada).IsRequired();
        builder.Property(x => x.FechaInicio);
        builder.Property(x => x.FechaCierre);
        builder.Property(x => x.ResponsableId).IsRequired();
        builder.Property(x => x.SnapshotCapturadoAt);
        builder.Property(x => x.AprobadorId);
        builder.Property(x => x.FechaAprobacion);
        builder.Property(x => x.MotivoRechazo).HasMaxLength(500);

        // §5.2: bandeja "Conteos en curso".
        builder.HasIndex(x => new { x.Estado, x.FechaPlanificada })
            .HasDatabaseName("ix_conteos_activos")
            .HasFilter("estado IN (0, 1, 2, 3)"); // Planificado/EnCurso/EnConciliacion/Aprobado

        builder.HasMany(x => x.Lineas)
            .WithOne()
            .HasForeignKey(l => l.ConteoId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class LineaConteoConfiguration : IEntityTypeConfiguration<LineaConteo>
{
    public void Configure(EntityTypeBuilder<LineaConteo> builder)
    {
        builder.ToTable("lineas_conteo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ConteoId).IsRequired();
        builder.Property(x => x.ArticuloId).IsRequired();
        builder.Property(x => x.SubAlmacenId).IsRequired();
        builder.Property(x => x.UbicacionId).IsRequired();
        builder.Property(x => x.CantidadTeorica).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.CostoPromedioSnapshot).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.CantidadRealCapturada).HasPrecision(14, 4);
        builder.Property(x => x.CapturadoPor);
        builder.Property(x => x.CapturadoAt);
        builder.Property(x => x.RequiereRecuento).IsRequired();
        builder.Property(x => x.AprobadoIndividualmente).IsRequired();
        builder.Property(x => x.Justificacion).HasMaxLength(500);

        // C7.2c: el conteo baja a rack. Índice único por (conteo, artículo,
        // ubicación) — espeja la PK de saldos por ubicación, así que el
        // snapshot (una línea por fila de saldo) no puede violarlo.
        builder.HasIndex(x => new { x.ConteoId, x.ArticuloId, x.UbicacionId })
            .IsUnique()
            .HasDatabaseName("ux_lineas_conteo");
        builder.HasIndex(x => x.ConteoId);

        // FK física a almacen.ubicaciones (mismo esquema). Restrict: no borrar
        // una ubicación referenciada por líneas de conteo.
        builder.HasOne<Ubicacion>()
            .WithMany()
            .HasForeignKey(x => x.UbicacionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RecuentoConteoConfiguration : IEntityTypeConfiguration<RecuentoConteo>
{
    public void Configure(EntityTypeBuilder<RecuentoConteo> builder)
    {
        builder.ToTable("recuentos_conteo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.LineaConteoId).IsRequired();
        builder.Property(x => x.Secuencia).IsRequired();
        builder.Property(x => x.CantidadRecontada).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.CapturadoPor).IsRequired();
        builder.Property(x => x.CapturadoAt).IsRequired();

        builder.HasIndex(x => new { x.LineaConteoId, x.Secuencia })
            .IsUnique()
            .HasDatabaseName("ux_recuentos_secuencia");
    }
}

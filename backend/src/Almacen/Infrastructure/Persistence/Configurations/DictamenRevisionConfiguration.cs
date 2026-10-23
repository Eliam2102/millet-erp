using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Revision;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>Configuración EF Core de <see cref="DictamenRevision"/> (ALM-04). Tabla <c>almacen.dictamenes_revision</c>.</summary>
public sealed class DictamenRevisionConfiguration : IEntityTypeConfiguration<DictamenRevision>
{
    public void Configure(EntityTypeBuilder<DictamenRevision> builder)
    {
        builder.ToTable("dictamenes_revision", t =>
        {
            t.HasCheckConstraint("ck_dictamenes_revision_cantidad", "cantidad > 0");
            t.HasCheckConstraint("ck_dictamenes_revision_resultado", "resultado BETWEEN 0 AND 2");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmpresaId).IsRequired();
        builder.Property(x => x.LineaRevisionId).IsRequired();
        builder.Property(x => x.MovimientoRevisionId).IsRequired();
        builder.Property(x => x.Resultado).HasConversion<short>().IsRequired();
        builder.Property(x => x.Cantidad).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.Motivo).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DictaminadoPor).IsRequired();
        builder.Property(x => x.DictaminadoAt).IsRequired();
        builder.Property(x => x.MovimientoSalidaRevisionId);
        builder.Property(x => x.MovimientoEntradaId);
        builder.Property(x => x.DevolucionAProveedorId);

        builder.HasOne<LineaMovimiento>()
            .WithMany()
            .HasForeignKey(x => x.LineaRevisionId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.LineaRevisionId).HasDatabaseName("ix_dictamenes_revision_linea");
    }
}

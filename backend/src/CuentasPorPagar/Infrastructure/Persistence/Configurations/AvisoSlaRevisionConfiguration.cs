using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class AvisoSlaRevisionConfiguration : IEntityTypeConfiguration<AvisoSlaRevision>
{
    public void Configure(EntityTypeBuilder<AvisoSlaRevision> builder)
    {
        builder.ToTable("avisos_sla_revision");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.DependenciaRevisoraId).IsRequired();
        builder.Property(e => e.MotivoRevisionId).IsRequired();
        builder.Property(e => e.Nivel).HasConversion<int>().IsRequired();
        builder.Property(e => e.Fecha).IsRequired();
        builder.Property(e => e.DiasHabiles).IsRequired();
        builder.Property(e => e.SlaDias).IsRequired();
        builder.Property(e => e.FechaEntradaRevision).IsRequired();
        builder.Property(e => e.EmitidoEn).IsRequired();

        // Sin FK a facturas_proveedor: el registro es histórico.
        builder.HasIndex(e => new { e.FacturaProveedorId, e.Nivel, e.Fecha })
            .IsUnique()
            .HasDatabaseName("ux_avisos_sla_revision_factura_nivel_fecha");

        builder.HasIndex(e => new { e.FacturaProveedorId, e.FechaEntradaRevision })
            .HasDatabaseName("ix_avisos_sla_revision_factura_entrada");
    }
}

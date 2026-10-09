using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

public sealed class SolicitudCancelacionOcConfiguration : IEntityTypeConfiguration<SolicitudCancelacionOc>
{
    public void Configure(EntityTypeBuilder<SolicitudCancelacionOc> builder)
    {
        builder.ToTable("oc_solicitudes_cancelacion");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Ignore(s => s.AggregateRootId);
        builder.Property(s => s.EstadoAnterior).HasConversion<short>();
        builder.Property(s => s.MotivoSolicitud).HasMaxLength(500).IsRequired();
        builder.Property(s => s.MotivoResolucion).HasMaxLength(500);
        builder.HasIndex(s => s.OrdenCompraId).IsUnique()
            .HasFilter("fecha_resolucion IS NULL");
    }
}

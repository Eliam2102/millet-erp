using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Eventos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

public sealed class EventoProcesadoConfiguration : IEntityTypeConfiguration<EventoProcesado>
{
    public void Configure(EntityTypeBuilder<EventoProcesado> builder)
    {
        builder.ToTable("evento_procesado");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EventoId).IsRequired();
        builder.Property(e => e.EventoTipo).HasMaxLength(150).IsRequired();
        builder.Property(e => e.ProcesadoEn).IsRequired();
        builder.Property(e => e.Detalle).HasMaxLength(1000);

        builder.HasIndex(e => new { e.EventoId, e.EventoTipo })
            .HasDatabaseName("ux_evento_procesado_id_tipo")
            .IsUnique();
    }
}

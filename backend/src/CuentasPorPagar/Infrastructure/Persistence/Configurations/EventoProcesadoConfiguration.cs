using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Eventos;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class EventoProcesadoConfiguration : IEntityTypeConfiguration<EventoProcesado>
{
    public void Configure(EntityTypeBuilder<EventoProcesado> builder)
    {
        builder.ToTable("eventos_procesados");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EventoId).IsRequired();
        builder.Property(e => e.EventoTipo).HasMaxLength(150).IsRequired();
        builder.Property(e => e.ProcesadoEn).IsRequired();
        builder.Property(e => e.Detalle).HasMaxLength(1000);

        builder.HasIndex(e => new { e.EventoId, e.EventoTipo })
            .HasDatabaseName("ux_eventos_procesados_id_tipo")
            .IsUnique();
    }
}

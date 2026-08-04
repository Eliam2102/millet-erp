using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Idempotencia;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="EventoProcesado"/>. Tabla
/// <c>compras.eventos_procesados</c>. PK compuesto
/// <c>(evento_id, evento_tipo)</c> — un mismo GUID puede aparecer en
/// múltiples eventos de distinto tipo y queremos dedupe por par.
/// </summary>
public sealed class EventoProcesadoConfiguration : IEntityTypeConfiguration<EventoProcesado>
{
    public void Configure(EntityTypeBuilder<EventoProcesado> builder)
    {
        builder.ToTable("eventos_procesados");
        builder.HasKey(x => new { x.EventoId, x.EventoTipo });

        builder.Property(x => x.EventoId).IsRequired();
        builder.Property(x => x.EventoTipo).HasMaxLength(150).IsRequired();
        builder.Property(x => x.ProcesadoAt).IsRequired();
        builder.Property(x => x.Observaciones).HasMaxLength(500);

        builder.HasIndex(x => x.ProcesadoAt)
            .HasDatabaseName("ix_eventos_procesados_at");
    }
}

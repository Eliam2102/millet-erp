using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Envio"/>. Tabla
/// <c>integraciones_aw.envio</c>. FK con cascade hacia
/// <see cref="EntidadExterna"/>.
/// </summary>
public sealed class EnvioConfiguration : IEntityTypeConfiguration<Envio>
{
    public void Configure(EntityTypeBuilder<Envio> builder)
    {
        builder.ToTable("envio");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EntidadExternaId).IsRequired();
        builder.Property(e => e.AttemptNumber).IsRequired();
        builder.Property(e => e.StartedAt).IsRequired();
        builder.Property(e => e.FinishedAt);
        builder.Property(e => e.Status).HasConversion<short>().IsRequired();
        builder.Property(e => e.DropServiceUrl).HasMaxLength(200).IsRequired();
        builder.Property(e => e.BytesSent);
        builder.Property(e => e.HttpStatusCode);
        builder.Property(e => e.ErrorMessage).HasColumnType("text");
        builder.Property(e => e.ErrorKind).HasMaxLength(40);
        builder.Property(e => e.DurationMs);
        // Nullable: registros pre-feature (creados antes del wireado de Envio
        // en AwDropWorker) quedan sin filename. El late-reconciler los ignora.
        builder.Property(e => e.Filename).HasMaxLength(200);

        builder.HasOne<EntidadExterna>()
            .WithMany()
            .HasForeignKey(e => e.EntidadExternaId)
            .OnDelete(DeleteBehavior.Cascade);

        // Índice principal para timeline / debug de la entidad.
        builder.HasIndex(e => new { e.EntidadExternaId, e.AttemptNumber })
            .HasDatabaseName("ix_envio_entidad_attempt");

        // Índice para dashboards de fallas recientes. Filtro parcial:
        // status=2 (Failed). Valores estables del enum.
        builder.HasIndex(e => e.StartedAt)
            .HasDatabaseName("ix_envio_failed_recent")
            .HasFilter("status = 2");
    }
}

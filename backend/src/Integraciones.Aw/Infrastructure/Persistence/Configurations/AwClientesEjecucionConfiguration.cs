using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Configurations;

/// <summary>Tablas <c>integraciones_aw.aw_clientes_ejecucion</c> y <c>..._error</c> (ADM-06).</summary>
public sealed class AwClientesEjecucionConfiguration : IEntityTypeConfiguration<AwClientesEjecucion>
{
    public void Configure(EntityTypeBuilder<AwClientesEjecucion> builder)
    {
        builder.ToTable("aw_clientes_ejecucion");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Origen).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.CursorActual).HasMaxLength(40);
        builder.Property(e => e.Actor).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ErrorGeneral).HasMaxLength(AwClientesEjecucion.MensajeErrorMaxLength);

        builder.HasMany(e => e.ErroresPorReferencia)
            .WithOne()
            .HasForeignKey(x => x.EjecucionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.ErroresPorReferencia).HasField("_errores").UsePropertyAccessMode(PropertyAccessMode.Field);

        // Máx. un barrido vivo (Pendiente=0 / EnCurso=1) por origen: impide dos barridos simultáneos.
        // Tipo=0 es Barrido; los reintentos por referencia (Tipo=1) no compiten por el cupo.
        builder.HasIndex(e => e.Origen)
            .IsUnique()
            .HasDatabaseName("uq_aw_clientes_ejecucion_barrido_vivo")
            .HasFilter("tipo = 0 AND estado IN (0, 1)");

        builder.HasIndex(e => e.IniciadaEnUtc).HasDatabaseName("ix_aw_clientes_ejecucion_iniciada");
    }
}

public sealed class AwClientesEjecucionErrorConfiguration : IEntityTypeConfiguration<AwClientesEjecucionError>
{
    public void Configure(EntityTypeBuilder<AwClientesEjecucionError> builder)
    {
        builder.ToTable("aw_clientes_ejecucion_error");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Referencia).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Codigo).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Mensaje).HasMaxLength(AwClientesEjecucion.MensajeErrorMaxLength).IsRequired();
        builder.HasIndex(e => new { e.EjecucionId, e.Referencia }).HasDatabaseName("ix_aw_clientes_ejecucion_error_ref");
    }
}

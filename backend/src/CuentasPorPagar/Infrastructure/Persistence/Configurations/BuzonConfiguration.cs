using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Mailbox;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class BuzonRevisionCfdiConfiguration : IEntityTypeConfiguration<BuzonRevisionCfdi>
{
    public void Configure(EntityTypeBuilder<BuzonRevisionCfdi> builder)
    {
        builder.ToTable("buzon_revision_cfdi");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MensajeId).HasMaxLength(BuzonRevisionCfdi.MaxMensajeId).IsRequired();
        builder.Property(e => e.Asunto).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Remitente).HasMaxLength(320).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(60).IsRequired();
        builder.Property(e => e.MotivoDescarte).HasMaxLength(400);
        // Idempotencia: un correo, una fila de revisión.
        builder.HasIndex(e => e.MensajeId).IsUnique().HasDatabaseName("ux_buzon_revision_cfdi_mensaje");
        builder.HasIndex(e => new { e.Descartado, e.RegistradoEn }).HasDatabaseName("ix_buzon_revision_cfdi_estado_fecha");
    }
}

public sealed class BuzonEjecucionConfiguration : IEntityTypeConfiguration<BuzonEjecucion>
{
    public void Configure(EntityTypeBuilder<BuzonEjecucion> builder)
    {
        builder.ToTable("buzon_ejecuciones");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Error).HasMaxLength(60);
        builder.HasIndex(e => e.InicioUtc).HasDatabaseName("ix_buzon_ejecuciones_inicio");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.Alertas;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class AlertaCarteraConfiguration : IEntityTypeConfiguration<AlertaCartera>
{
    public void Configure(EntityTypeBuilder<AlertaCartera> builder)
    {
        builder.ToTable("alerta_cartera", t =>
        {
            t.HasCheckConstraint("ck_alerta_cartera_tipo", "tipo IN (1, 2, 3)");
            t.HasCheckConstraint(
                "ck_alerta_cartera_atencion_consistente",
                "(atendida = TRUE AND atendida_por IS NOT NULL AND atendida_en IS NOT NULL) OR " +
                "(atendida = FALSE AND atendida_por IS NULL AND atendida_en IS NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Detalle).HasMaxLength(500).IsRequired();
        builder.Property(e => e.DisparadaEn).IsRequired();
        builder.Property(e => e.Atendida).IsRequired();
        builder.Property(e => e.AtendidaPor);
        builder.Property(e => e.AtendidaEn);

        // Bandeja de pendientes + dedupe del worker.
        builder.HasIndex(e => new { e.ClienteId, e.Tipo, e.Moneda })
            .HasDatabaseName("ix_alerta_cartera_dedupe")
            .HasFilter("atendida = FALSE");

        builder.HasIndex(e => e.DisparadaEn)
            .HasDatabaseName("ix_alerta_cartera_disparada");
    }
}

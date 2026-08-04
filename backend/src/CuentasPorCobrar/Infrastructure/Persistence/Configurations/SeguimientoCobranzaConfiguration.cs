using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.Cobranza;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class SeguimientoCobranzaConfiguration : IEntityTypeConfiguration<SeguimientoCobranza>
{
    public void Configure(EntityTypeBuilder<SeguimientoCobranza> builder)
    {
        builder.ToTable("seguimiento_cobranza", t =>
        {
            t.HasCheckConstraint("ck_seguimiento_cobranza_canal", "canal IN (1, 2, 3)");
            t.HasCheckConstraint("ck_seguimiento_cobranza_resultado", "resultado IN (1, 2, 3, 4)");
            // §4.2: promesa_pago (1) exige monto + fecha; los demás no los llevan.
            t.HasCheckConstraint(
                "ck_seguimiento_cobranza_promesa_consistente",
                "(resultado = 1 AND monto_comprometido IS NOT NULL AND monto_comprometido > 0 AND fecha_comprometida IS NOT NULL) OR " +
                "(resultado != 1 AND monto_comprometido IS NULL AND fecha_comprometida IS NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.Fecha).IsRequired();
        builder.Property(e => e.UsuarioId).IsRequired();

        builder.Property(e => e.Canal).HasConversion<short>().IsRequired();
        builder.Property(e => e.Resultado).HasConversion<short>().IsRequired();

        builder.Property(e => e.MontoComprometido).HasPrecision(14, 2);
        builder.Property(e => e.FechaComprometida);
        builder.Property(e => e.Nota).HasMaxLength(2000).IsRequired();

        // §5.1 del 01-diseño: seguimiento_cobranza (cliente_id, fecha DESC).
        builder.HasIndex(e => new { e.ClienteId, e.Fecha })
            .HasDatabaseName("ix_seguimiento_cobranza_cliente_fecha")
            .IsDescending(false, true);

        // Promesas vigentes para alertas de cartera (CXC-PR8).
        builder.HasIndex(e => e.FechaComprometida)
            .HasDatabaseName("ix_seguimiento_cobranza_promesas")
            .HasFilter("resultado = 1");
    }
}

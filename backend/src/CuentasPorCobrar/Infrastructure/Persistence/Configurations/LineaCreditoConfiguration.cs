using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.LineaCredito;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class LineaCreditoConfiguration : IEntityTypeConfiguration<LineaCredito>
{
    public void Configure(EntityTypeBuilder<LineaCredito> builder)
    {
        builder.ToTable("linea_credito", t =>
        {
            t.HasCheckConstraint("ck_linea_credito_limite_positivo", "limite > 0");
            t.HasCheckConstraint("ck_linea_credito_plazo_positivo", "plazo_dias > 0");
            t.HasCheckConstraint("ck_linea_credito_moneda", "moneda IN ('MXN', 'USD')");
            t.HasCheckConstraint("ck_linea_credito_estado", "estado IN (1, 2, 3)");
            t.HasCheckConstraint("ck_linea_credito_origen", "origen IN (1, 2)");
            t.HasCheckConstraint(
                "ck_linea_credito_clasificacion",
                "clasificacion IS NULL OR clasificacion IN ('A', 'B', 'C', 'E')");
            t.HasCheckConstraint(
                "ck_linea_credito_bloqueo_consistente",
                "(estado = 2 AND motivo_bloqueo IS NOT NULL) OR (estado != 2)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Limite).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.PlazoDias).IsRequired();

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.MotivoBloqueo).HasMaxLength(400);
        builder.Property(e => e.Clasificacion).HasMaxLength(1);

        // Invariante §4.2 / índice crítico §5.1: una sola línea Activa por
        // (cliente, moneda). Backstop del check en CrearLineaCreditoHandler.
        builder.HasIndex(e => new { e.ClienteId, e.Moneda })
            .HasDatabaseName("ux_linea_credito_cliente_moneda_activa")
            .IsUnique()
            .HasFilter("estado = 1");

        builder.HasIndex(e => new { e.ClienteId, e.Estado })
            .HasDatabaseName("ix_linea_credito_cliente_estado");
    }
}

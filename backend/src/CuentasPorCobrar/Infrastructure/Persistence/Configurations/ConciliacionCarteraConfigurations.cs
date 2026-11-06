using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.Conciliacion;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class ConciliacionCarteraConfiguration : IEntityTypeConfiguration<ConciliacionCartera>
{
    public void Configure(EntityTypeBuilder<ConciliacionCartera> builder)
    {
        builder.ToTable("conciliacion_cartera", t =>
        {
            t.HasCheckConstraint("ck_conciliacion_cartera_origen", "origen IN (1, 2)");
            t.HasCheckConstraint("ck_conciliacion_cartera_conteos", "comprobantes_revisados >= 0 AND total_diferencias >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Fecha).IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.EjecutadaPor);
        builder.Property(e => e.EjecutadaEn).IsRequired();
        builder.Property(e => e.ComprobantesRevisados).IsRequired();
        builder.Property(e => e.TotalDiferencias).IsRequired();

        builder.HasMany(e => e.Diferencias)
            .WithOne()
            .HasForeignKey(d => d.ConciliacionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Diferencias).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Bandeja (más reciente primero) y «ya corrió la programada de ese día».
        builder.HasIndex(e => new { e.EmpresaId, e.Fecha, e.Origen })
            .HasDatabaseName("ix_conciliacion_cartera_empresa_fecha_origen");
    }
}

public sealed class DiferenciaConciliacionConfiguration : IEntityTypeConfiguration<DiferenciaConciliacion>
{
    public void Configure(EntityTypeBuilder<DiferenciaConciliacion> builder)
    {
        builder.ToTable("conciliacion_cartera_diferencia", t =>
            t.HasCheckConstraint("ck_conciliacion_cartera_diferencia_tipo", "tipo BETWEEN 1 AND 6"));

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ConciliacionId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.ComprobanteId).IsRequired();
        builder.Property(e => e.TipoComprobante).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Folio).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Uuid).HasMaxLength(40);
        builder.Property(e => e.FacturaVentaId).IsRequired();
        builder.Property(e => e.ReceptorRfc).HasMaxLength(13).IsRequired();
        builder.Property(e => e.ReceptorNombre).HasMaxLength(300).IsRequired();
        builder.Property(e => e.ImporteFacturacion).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.ImporteCartera).HasPrecision(14, 2);
        builder.Property(e => e.CausaProbable).HasMaxLength(300).IsRequired();

        builder.HasIndex(e => e.ConciliacionId).HasDatabaseName("ix_conciliacion_cartera_diferencia_conciliacion");
    }
}

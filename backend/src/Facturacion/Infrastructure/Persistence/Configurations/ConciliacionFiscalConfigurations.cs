using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Conciliacion;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>M-314 · <c>facturacion.resoluciones_incidencia_fiscal</c>: una fila por resolución; índice por comprobante.</summary>
public sealed class ResolucionIncidenciaFiscalConfiguration : IEntityTypeConfiguration<ResolucionIncidenciaFiscal>
{
    public void Configure(EntityTypeBuilder<ResolucionIncidenciaFiscal> builder)
    {
        builder.ToTable("resoluciones_incidencia_fiscal");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ComprobanteId).IsRequired();
        builder.Property(e => e.Uuid).HasMaxLength(36).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.EstadoInterno).HasConversion<short>().IsRequired();
        builder.Property(e => e.EstatusSatAntes).HasMaxLength(40);
        builder.Property(e => e.EstatusSatDespues).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Soporte).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Nota).HasMaxLength(1000);
        builder.Property(e => e.ResueltaPor).HasMaxLength(256).IsRequired();
        builder.Property(e => e.ResueltaEn).IsRequired();
        builder.HasIndex(e => new { e.EmpresaId, e.ComprobanteId }).HasDatabaseName("ix_resoluciones_incidencia_fiscal_comprobante");
    }
}

/// <summary>
/// M-314 · <c>facturacion.cierres_folios</c>: un cierre vigente por empresa, tipo, serie y mes (índice único filtrado por vigente; al
/// reabrir queda la fila con su bitácora y se puede cerrar de nuevo).
/// </summary>
public sealed class CierreFoliosConfiguration : IEntityTypeConfiguration<CierreFolios>
{
    public void Configure(EntityTypeBuilder<CierreFolios> builder)
    {
        builder.ToTable("cierres_folios");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Anio).IsRequired();
        builder.Property(e => e.Mes).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Serie).HasMaxLength(40).IsRequired();
        builder.Property(e => e.ReporteJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.CerradoPor).HasMaxLength(256).IsRequired();
        builder.Property(e => e.ReabiertoPor).HasMaxLength(256);
        builder.Property(e => e.MotivoReapertura).HasMaxLength(500);
        builder.HasIndex(e => new { e.EmpresaId, e.Anio, e.Mes, e.Tipo, e.Serie }).IsUnique().HasFilter("vigente")
            .HasDatabaseName("ux_cierres_folios_vigente");
        builder.ToTable(t => t.HasCheckConstraint("ck_cierres_folios_mes", "mes BETWEEN 1 AND 12"));
    }
}

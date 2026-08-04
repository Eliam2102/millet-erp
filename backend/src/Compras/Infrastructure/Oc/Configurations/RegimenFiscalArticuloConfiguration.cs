using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="RegimenFiscalArticulo"/>
/// (tabla <c>compras.regimenes_fiscales_articulo</c>, F3-PR2). Seed
/// inicial con 4 combinaciones del catálogo §C3:
///
/// <list type="bullet">
///   <item>GENERAL + GENERAL → IVA 16%, sin retención.</item>
///   <item>GENERAL + EXENTO → IVA 0%, sin retención.</item>
///   <item>GENERAL + TASA_CERO → IVA 0%, sin retención (alias).</item>
///   <item>GENERAL + SERVICIO_PROFESIONAL → IVA 16%, retención ISR 10%.</item>
/// </list>
///
/// Las claves de regímenes son provisionales — el catálogo final se
/// completa con CxP y Contabilidad en F9 (catálogos seed).
/// </summary>
public sealed class RegimenFiscalArticuloConfiguration : IEntityTypeConfiguration<RegimenFiscalArticulo>
{
    public void Configure(EntityTypeBuilder<RegimenFiscalArticulo> builder)
    {
        builder.ToTable("regimenes_fiscales_articulo", t =>
        {
            t.HasCheckConstraint("ck_regfa_iva_rango", "iva_porcentaje BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_regfa_isr_rango",
                "retencion_isr_porcentaje IS NULL OR retencion_isr_porcentaje BETWEEN 0 AND 1");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.RegimenProveedor).HasMaxLength(60).IsRequired();
        builder.Property(r => r.RegimenArticulo).HasMaxLength(60).IsRequired();

        builder.Property(r => r.IvaPorcentaje).HasColumnType("numeric(5,4)").IsRequired();
        builder.Property(r => r.RetencionIsrPorcentaje).HasColumnType("numeric(5,4)");

        builder.Property(r => r.VigenteDesde).IsRequired();
        builder.Property(r => r.VigenteHasta);
        builder.Property(r => r.Activo).HasDefaultValue(true).IsRequired();

        // UNIQUE por par (proveedor, artículo). Lookup atómico del motor v1.
        builder.HasIndex(r => new { r.RegimenProveedor, r.RegimenArticulo })
            .IsUnique()
            .HasDatabaseName("uq_regfa_par");

        // Seed inicial (C3). Los GUIDs siguen el patrón
        // 00000003-0003-0020-* (namespace OC + sub 0020 para regímenes
        // fiscales) para idempotencia.
        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        builder.HasData(
            new
            {
                Id = Guid.Parse("00000003-0003-0020-0000-000000000001"),
                RegimenProveedor = "GENERAL",
                RegimenArticulo = "GENERAL",
                IvaPorcentaje = 0.16m,
                RetencionIsrPorcentaje = (decimal?)null,
                VigenteDesde = seedTime,
                VigenteHasta = (DateTimeOffset?)null,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0020-0000-000000000002"),
                RegimenProveedor = "GENERAL",
                RegimenArticulo = "EXENTO",
                IvaPorcentaje = 0m,
                RetencionIsrPorcentaje = (decimal?)null,
                VigenteDesde = seedTime,
                VigenteHasta = (DateTimeOffset?)null,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0020-0000-000000000003"),
                RegimenProveedor = "GENERAL",
                RegimenArticulo = "TASA_CERO",
                IvaPorcentaje = 0m,
                RetencionIsrPorcentaje = (decimal?)null,
                VigenteDesde = seedTime,
                VigenteHasta = (DateTimeOffset?)null,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0020-0000-000000000004"),
                RegimenProveedor = "GENERAL",
                RegimenArticulo = "SERVICIO_PROFESIONAL",
                IvaPorcentaje = 0.16m,
                RetencionIsrPorcentaje = (decimal?)0.10m,
                VigenteDesde = seedTime,
                VigenteHasta = (DateTimeOffset?)null,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            });
    }
}

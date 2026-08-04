using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Pasivos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="PasivoPendientePago"/> —
/// proyección de la bandeja de egresos (§4.4). Una fila por
/// <c>factura_proveedor_id</c> vía índice único (patrón
/// <c>RecepcionOcLocal</c> de CxP: PK sintética de <c>BaseEntity</c> +
/// unicidad de negocio por índice).
/// </summary>
public sealed class PasivoPendientePagoConfiguration : IEntityTypeConfiguration<PasivoPendientePago>
{
    public void Configure(EntityTypeBuilder<PasivoPendientePago> builder)
    {
        builder.ToTable("pasivo_pendiente_pago", t =>
        {
            t.HasCheckConstraint("ck_pasivo_pendiente_saldo_no_negativo", "saldo_pendiente >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.ProveedorId).IsRequired();
        builder.Property(e => e.OrdenCompraId);
        builder.Property(e => e.MontoTotal).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.SaldoPendiente).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoCambio).HasPrecision(12, 6);
        builder.Property(e => e.FechaVencimiento).IsRequired();
        builder.Property(e => e.UuidCfdi);
        builder.Property(e => e.FolioProveedor).HasMaxLength(60);
        builder.Property(e => e.MetodoPago).HasMaxLength(3);
        builder.Property(e => e.RecibidoEn).IsRequired();

        // GI-PR2 (doc 12): pasivos internos. Backfill en la migration:
        // Proveedor/Factura para las filas existentes.
        builder.Property(e => e.TipoBeneficiario).HasMaxLength(20).IsRequired();
        builder.Property(e => e.BeneficiarioId);
        builder.Property(e => e.OrigenTipo).HasMaxLength(30).IsRequired();
        builder.Property(e => e.OrigenId).IsRequired();

        builder.HasIndex(e => e.TipoBeneficiario)
            .HasDatabaseName("ix_pasivo_tipo_beneficiario");

        builder.HasIndex(e => e.FacturaProveedorId)
            .HasDatabaseName("ux_pasivo_pendiente_factura")
            .IsUnique();

        // §5.1: bandeja por vencimiento y por proveedor.
        builder.HasIndex(e => new { e.EmpresaId, e.FechaVencimiento })
            .HasDatabaseName("ix_pasivo_pendiente_empresa_vencimiento");

        builder.HasIndex(e => e.ProveedorId)
            .HasDatabaseName("ix_pasivo_pendiente_proveedor");
    }
}

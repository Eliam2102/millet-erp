using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class AnticipoProveedorConfiguration : IEntityTypeConfiguration<AnticipoProveedor>
{
    public void Configure(EntityTypeBuilder<AnticipoProveedor> builder)
    {
        builder.ToTable("anticipos_proveedor", t =>
        {
            t.HasCheckConstraint("ck_anticipo_monto_entregado_positivo", "monto_entregado > 0");
            t.HasCheckConstraint("ck_anticipo_monto_amortizado_valido",
                "monto_amortizado >= 0 AND monto_amortizado <= monto_entregado");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CfdiRecibidoId);

        builder.Property(e => e.UuidCfdi).HasMaxLength(36).IsRequired();
        builder.Property(e => e.ProveedorId).IsRequired();
        builder.Property(e => e.Serie).HasMaxLength(10).IsRequired();
        builder.Property(e => e.FolioProveedor).HasMaxLength(40);

        builder.Property(e => e.FechaCfdi).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoCambio).HasPrecision(18, 6);

        builder.Property(e => e.MontoEntregado).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.MontoAmortizado).HasPrecision(18, 4).IsRequired();
        builder.Ignore(e => e.SaldoAmortizable);

        builder.Property(e => e.OrdenCompraId);
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.FechaCaptura).IsRequired();
        builder.Property(e => e.FechaAmortizacion);
        builder.Property(e => e.FechaCancelacion);
        builder.Property(e => e.MotivoCancelacion).HasMaxLength(400);
        builder.Property(e => e.CapturadoPor);

        builder.HasIndex(e => e.UuidCfdi)
            .HasDatabaseName("ux_anticipos_proveedor_uuid")
            .IsUnique();

        builder.HasIndex(e => new { e.ProveedorId, e.Estado })
            .HasDatabaseName("ix_anticipos_proveedor_estado");

        builder.HasIndex(e => e.OrdenCompraId)
            .HasDatabaseName("ix_anticipos_proveedor_oc")
            .HasFilter("orden_compra_id IS NOT NULL");
    }
}

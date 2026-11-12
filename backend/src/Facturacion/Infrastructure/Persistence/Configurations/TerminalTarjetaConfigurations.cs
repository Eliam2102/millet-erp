using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Cajas.Terminal;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

public sealed class CargaTerminalConfiguration : IEntityTypeConfiguration<CargaTerminal>
{
    public void Configure(EntityTypeBuilder<CargaTerminal> builder)
    {
        builder.ToTable("terminal_carga");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.NombreArchivo).HasMaxLength(260).IsRequired();
        builder.Property(e => e.Huella).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => new { e.EmpresaId, e.CargadoEn }).HasDatabaseName("ix_terminal_carga_empresa_fecha");
    }
}

public sealed class TransaccionTerminalConfiguration : IEntityTypeConfiguration<TransaccionTerminal>
{
    public void Configure(EntityTypeBuilder<TransaccionTerminal> builder)
    {
        builder.ToTable("terminal_transaccion", t =>
            t.HasCheckConstraint("ck_terminal_transaccion_estado", "estado IN (1, 2, 3)"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.NoOperacion).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2);
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Ultimos4).HasMaxLength(4);
        builder.Property(e => e.Autorizacion).HasMaxLength(20);
        builder.Property(e => e.Estatus).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Comision).HasPrecision(18, 2);
        builder.Property(e => e.IvaComision).HasPrecision(18, 2);
        builder.Property(e => e.Neto).HasPrecision(18, 2);
        builder.Property(e => e.FolioLiquidacion).HasMaxLength(40);
        builder.Property(e => e.FechaLiquidacion);
        builder.Property(e => e.Afiliacion).HasMaxLength(20);
        builder.Property(e => e.CodTerminal).HasMaxLength(20);
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Ignore(e => e.TraeLiquidacion);

        builder.HasOne<CargaTerminal>().WithMany().HasForeignKey(e => e.CargaId).OnDelete(DeleteBehavior.Restrict);

        // Recargar el mismo archivo no duplica renglones (criterio 2).
        builder.HasIndex(e => new { e.EmpresaId, e.NoOperacion }).IsUnique()
            .HasDatabaseName("ux_terminal_transaccion_empresa_operacion");
        // Una forma de pago del cobro se empareja con una sola venta de la terminal.
        builder.HasIndex(e => e.CobroFormaPagoId).IsUnique().HasFilter("cobro_forma_pago_id IS NOT NULL")
            .HasDatabaseName("ux_terminal_transaccion_forma_pago");
        builder.HasIndex(e => new { e.EmpresaId, e.Estado }).HasDatabaseName("ix_terminal_transaccion_empresa_estado");
    }
}

public sealed class LiquidacionTerminalConfiguration : IEntityTypeConfiguration<LiquidacionTerminal>
{
    public void Configure(EntityTypeBuilder<LiquidacionTerminal> builder)
    {
        builder.ToTable("terminal_liquidacion");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Folio).HasMaxLength(40).IsRequired();
        builder.Property(e => e.Bruto).HasPrecision(18, 2);
        builder.Property(e => e.Comision).HasPrecision(18, 2);
        builder.Property(e => e.IvaComision).HasPrecision(18, 2);
        builder.Property(e => e.Neto).HasPrecision(18, 2);
        builder.HasIndex(e => new { e.EmpresaId, e.Folio }).IsUnique().HasDatabaseName("ux_terminal_liquidacion_empresa_folio");
        builder.Property(e => e.UuidFacturaBanco).HasMaxLength(36);
        builder.HasOne<FacturaBancoTerminal>().WithMany().HasForeignKey(e => e.FacturaBancoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.EmpresaId, e.Fecha }).HasFilter("factura_banco_id IS NULL")
            .HasDatabaseName("ix_terminal_liquidacion_sin_factura_fecha");
    }
}

public sealed class FacturaBancoTerminalConfiguration : IEntityTypeConfiguration<FacturaBancoTerminal>
{
    public void Configure(EntityTypeBuilder<FacturaBancoTerminal> builder)
    {
        builder.ToTable("terminal_factura_banco", t =>
            t.HasCheckConstraint("ck_terminal_factura_banco_mes", "mes BETWEEN 1 AND 12"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.UuidCfdi).HasMaxLength(36).IsRequired();
        builder.Property(e => e.RfcEmisor).HasMaxLength(13).IsRequired();
        builder.Property(e => e.Comision).HasPrecision(18, 2);
        builder.Property(e => e.IvaComision).HasPrecision(18, 2);
        builder.Property(e => e.Total).HasPrecision(18, 2);
        builder.Property(e => e.ComisionFactura).HasPrecision(18, 2);
        builder.Property(e => e.IvaFactura).HasPrecision(18, 2);
        builder.Property(e => e.Redondeo).HasPrecision(18, 2);
        // Un CFDI del banco ampara un solo mes.
        builder.HasIndex(e => new { e.EmpresaId, e.UuidCfdi }).IsUnique().HasDatabaseName("ux_terminal_factura_banco_empresa_uuid");
        builder.HasIndex(e => new { e.EmpresaId, e.CfdiRecibidoId }).IsUnique().HasDatabaseName("ux_terminal_factura_banco_empresa_cfdi");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary><c>contabilidad.saldos_iva_documento</c> (C1.6): IVA de cada factura de proveedor, de venta o de anticipo y lo ya reclasificado.</summary>
public sealed class SaldoIvaDocumentoConfiguration : IEntityTypeConfiguration<SaldoIvaDocumento>
{
    public void Configure(EntityTypeBuilder<SaldoIvaDocumento> builder)
    {
        builder.ToTable("saldos_iva_documento");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Lado).HasConversion<short>();
        builder.Property(x => x.Estado).HasConversion<short>();
        builder.Property(x => x.OrigenModulo).HasMaxLength(60).IsRequired();
        builder.Property(x => x.OrigenTipo).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Folio).HasMaxLength(60);
        builder.Property(x => x.Uuid).HasMaxLength(40);
        builder.Property(x => x.MetodoPago).HasMaxLength(3);
        builder.Property(x => x.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(x => x.TipoCambio).HasPrecision(18, 6);
        builder.Property(x => x.Base).HasPrecision(18, 2);
        builder.Property(x => x.Iva).HasPrecision(18, 2);
        builder.Property(x => x.IvaRetenido).HasPrecision(18, 2);
        builder.Property(x => x.Aplicado).HasPrecision(18, 2);
        builder.Property(x => x.IvaReclasificado).HasPrecision(18, 2);
        builder.Property(x => x.IvaRetenidoReclasificado).HasPrecision(18, 2);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.Property(x => x.RetencionAlPago).HasDefaultValue(false);
        builder.OwnsMany(x => x.Retenciones, b =>
        {
            b.ToJson("retenciones");
            b.Ignore(r => r.Pendiente);
        });
        // M-209: base e IVA por tratamiento.
        builder.OwnsMany(x => x.Tratamientos, b =>
        {
            b.ToJson("tratamientos");
            b.Ignore(t => t.Pendiente);
        });
        builder.Ignore(x => x.Saldo);
        builder.Ignore(x => x.IvaPendiente);
        builder.Ignore(x => x.Saldado);
        builder.HasIndex(x => new { x.EmpresaId, x.OrigenModulo, x.OrigenTipo, x.DocumentoId }).IsUnique()
            .HasDatabaseName("ux_saldos_iva_documento_documento");
        builder.HasIndex(x => new { x.EmpresaId, x.Lado, x.Fecha }).HasDatabaseName("ix_saldos_iva_documento_lado_fecha");
    }
}

/// <summary><c>contabilidad.reclasificaciones_iva</c> (C1.6): IVA que cada pago o cobro pasó a pagado o cobrado; base de K10.7.</summary>
public sealed class ReclasificacionIvaConfiguration : IEntityTypeConfiguration<ReclasificacionIva>
{
    public void Configure(EntityTypeBuilder<ReclasificacionIva> builder)
    {
        builder.ToTable("reclasificaciones_iva");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Tipo).HasConversion<short>();
        builder.Property(x => x.PagoModulo).HasMaxLength(60).IsRequired();
        builder.Property(x => x.PagoTipo).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Importe).HasPrecision(18, 2);
        builder.Property(x => x.Iva).HasPrecision(18, 2);
        builder.Property(x => x.IvaRetenido).HasPrecision(18, 2);
        builder.Property(x => x.IvaMxn).HasPrecision(18, 2);
        builder.Property(x => x.IvaRetenidoMxn).HasPrecision(18, 2);
        builder.OwnsMany(x => x.Retenciones, b => b.ToJson("retenciones"));
        builder.OwnsMany(x => x.Tratamientos, b => b.ToJson("tratamientos"));
        builder.HasOne<SaldoIvaDocumento>().WithMany().HasForeignKey(x => x.SaldoIvaDocumentoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmpresaId, x.PagoModulo, x.PagoTipo, x.PagoId, x.SaldoIvaDocumentoId }).IsUnique()
            .HasDatabaseName("ux_reclasificaciones_iva_pago_documento");
        builder.HasIndex(x => new { x.EmpresaId, x.Fecha }).HasDatabaseName("ix_reclasificaciones_iva_fecha");
    }
}

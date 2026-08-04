using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="CobroMostrador"/> y sus formas de pago
/// (CAJAS-PR4, 12-cajas.md §7). Tablas <c>facturacion.cobro_mostrador</c> /
/// <c>cobro_mostrador_forma_pago</c>. FK física a <c>comprobante</c> y a
/// <c>caja_sesion</c> (mismo esquema).
/// </summary>
public sealed class CobroMostradorConfiguration : IEntityTypeConfiguration<CobroMostrador>
{
    public void Configure(EntityTypeBuilder<CobroMostrador> builder)
    {
        builder.ToTable("cobro_mostrador");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CajaSesionId).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.CanalVentaId);
        builder.Property(e => e.ComprobanteId).IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.FechaCobro).IsRequired();
        builder.Property(e => e.UsuarioCobradorId).IsRequired();

        builder.HasOne<CajaSesion>()
            .WithMany()
            .HasForeignKey(e => e.CajaSesionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Comprobante>()
            .WithMany()
            .HasForeignKey(e => e.ComprobanteId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.FormasPago)
            .WithOne()
            .HasForeignKey(f => f.CobroMostradorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.FormasPago).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Un comprobante admite un solo cobro vigente (re-cobrar exige cancelar).
        builder.HasIndex(e => e.ComprobanteId)
            .IsUnique()
            .HasFilter("estado = 1")
            .HasDatabaseName("ux_cobro_mostrador_comprobante_vigente");

        builder.HasIndex(e => e.CajaSesionId).HasDatabaseName("ix_cobro_mostrador_sesion");
    }
}

public sealed class CobroMostradorFormaPagoConfiguration : IEntityTypeConfiguration<CobroMostradorFormaPago>
{
    public void Configure(EntityTypeBuilder<CobroMostradorFormaPago> builder)
    {
        builder.ToTable("cobro_mostrador_forma_pago");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.FormaPago).HasMaxLength(2).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Referencia).HasMaxLength(100);
        builder.Property(e => e.CuentaOrdenante).HasMaxLength(50);
        builder.Property(e => e.CuentaBeneficiaria).HasMaxLength(50);

        builder.HasIndex(e => e.CobroMostradorId).HasDatabaseName("ix_cobro_forma_pago_cobro");
    }
}

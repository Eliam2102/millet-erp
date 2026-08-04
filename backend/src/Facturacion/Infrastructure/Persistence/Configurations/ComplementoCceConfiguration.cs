using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Cce;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ComplementoCce"/> (F7-PR1). Tabla
/// <c>facturacion.complemento_cce</c> — hija 1:1 de <c>factura_venta</c> (la
/// relación la declara <c>FacturaVentaConfiguration</c>).
/// </summary>
public sealed class ComplementoCceConfiguration : IEntityTypeConfiguration<ComplementoCce>
{
    public void Configure(EntityTypeBuilder<ComplementoCce> builder)
    {
        builder.ToTable("complemento_cce");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.FacturaVentaId).IsRequired();
        builder.Property(e => e.TipoOperacion).HasMaxLength(2).IsRequired();
        builder.Property(e => e.Incoterm).HasMaxLength(10).IsRequired();
        builder.Property(e => e.TcDof).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.ReceptorNumRegIdTrib).HasMaxLength(40).IsRequired();
        builder.Property(e => e.ReceptorPaisResidencia).HasMaxLength(3).IsRequired();
        builder.Property(e => e.ClaveDePedimento).HasMaxLength(2);
        builder.Property(e => e.CertificadoOrigen).IsRequired();
        builder.Property(e => e.ReceptorDomicilioCalle).HasMaxLength(200);
        builder.Property(e => e.ReceptorDomicilioEstado).HasMaxLength(30);
        builder.Property(e => e.ReceptorDomicilioCodigoPostal).HasMaxLength(12);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.ComplementoCceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.FacturaVentaId)
            .IsUnique()
            .HasDatabaseName("ix_complemento_cce_factura");
    }
}

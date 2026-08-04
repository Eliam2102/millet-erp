using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Anticipos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Anticipo"/> (F4-PR1). Tabla
/// <c>facturacion.anticipo</c> — agregado del saldo amortizable, separado del
/// CFDI inmutable. <c>EmpresaId</c> y el soft-delete los aplica el
/// <c>BaseDbContext</c> (query filters); el <c>row_version</c> lo gestiona el
/// interceptor de concurrencia.
/// </summary>
public sealed class AnticipoConfiguration : IEntityTypeConfiguration<Anticipo>
{
    public void Configure(EntityTypeBuilder<Anticipo> builder)
    {
        builder.ToTable("anticipo");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.ReceptorRfc).HasMaxLength(13).IsRequired();

        builder.Property(e => e.TipoAnticipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();

        builder.Property(e => e.MontoCobrado).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.MontoAmortizado).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Saldo).HasPrecision(18, 2).IsRequired();

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.FacturaAnticipoId).IsRequired();
        builder.Property(e => e.PedidoOrigenRef).HasMaxLength(50);
        builder.Property(e => e.ObraId);
        builder.Property(e => e.ObraNombre).HasMaxLength(200);

        builder.HasMany(e => e.Vinculaciones)
            .WithOne()
            .HasForeignKey(v => v.AnticipoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.FacturaAnticipoId)
            .IsUnique()
            .HasDatabaseName("ix_anticipo_factura_anticipo");

        // Control de Anticipos: filtra por cliente + estado (§6.7).
        builder.HasIndex(e => new { e.EmpresaId, e.ClienteId, e.Estado })
            .HasDatabaseName("ix_anticipo_cliente_estado");
    }
}

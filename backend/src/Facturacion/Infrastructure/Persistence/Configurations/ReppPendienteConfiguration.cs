using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Repp;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

public sealed class ReppPendienteConfiguration : IEntityTypeConfiguration<ReppPendiente>
{
    public void Configure(EntityTypeBuilder<ReppPendiente> builder)
    {
        builder.ToTable("repp_pendiente");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Monto).HasPrecision(18, 6);
        builder.Property(p => p.Moneda).HasMaxLength(3);
        builder.Property(p => p.FormaPago).HasMaxLength(5);
        builder.Property(p => p.Estado).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => p.MovimientoBancarioId).IsUnique();
        builder.HasIndex(p => new { p.EmpresaId, p.Estado, p.FechaLimite });
        builder.HasIndex(p => p.IntentoReciboPagoId).IsUnique();
        builder.HasMany(p => p.Facturas).WithOne().HasForeignKey(f => f.ReppPendienteId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Facturas).UsePropertyAccessMode(PropertyAccessMode.Field);

    }
}

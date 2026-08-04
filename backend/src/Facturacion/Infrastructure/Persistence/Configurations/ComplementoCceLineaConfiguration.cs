using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Cce;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ComplementoCceLinea"/> (F7-PR1). Tabla
/// <c>facturacion.complemento_cce_linea</c> — mercancías del CCE.
/// </summary>
public sealed class ComplementoCceLineaConfiguration : IEntityTypeConfiguration<ComplementoCceLinea>
{
    public void Configure(EntityTypeBuilder<ComplementoCceLinea> builder)
    {
        builder.ToTable("complemento_cce_linea");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ComplementoCceId).IsRequired();
        builder.Property(e => e.FraccionArancelaria).HasMaxLength(10).IsRequired();
        builder.Property(e => e.UnidadAduana).HasMaxLength(3).IsRequired();
        builder.Property(e => e.CantidadAduana).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.ValorUnitarioAduana).HasPrecision(18, 6).IsRequired();
        builder.Property(e => e.ValorDolares).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.AplicaIva0).IsRequired();
    }
}

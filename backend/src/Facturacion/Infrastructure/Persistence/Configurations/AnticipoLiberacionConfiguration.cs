using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Anticipos;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>U-4: <c>facturacion.anticipo_liberacion</c>, hija del agregado <see cref="Anticipo"/> (sin query filter propio).</summary>
public sealed class AnticipoLiberacionConfiguration : IEntityTypeConfiguration<AnticipoLiberacion>
{
    public void Configure(EntityTypeBuilder<AnticipoLiberacion> builder)
    {
        builder.ToTable("anticipo_liberacion", t =>
            t.HasCheckConstraint("ck_anticipo_liberacion_motivo", "char_length(motivo) BETWEEN 5 AND 500"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.AnticipoId).IsRequired();
        builder.Property(e => e.FacturaVentaId).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(500).IsRequired();
        builder.Property(e => e.LiberadoPorId).IsRequired();
        builder.Property(e => e.LiberadoPorNombre).HasMaxLength(200).IsRequired();
        builder.Property(e => e.LiberadoEn).IsRequired();
        builder.HasIndex(e => e.AnticipoId).HasDatabaseName("ix_anticipo_liberacion_anticipo");
    }
}

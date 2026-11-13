using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Conciliaciones;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class PartidaTransitoLibroConfiguration : IEntityTypeConfiguration<PartidaTransitoLibro>
{
    public void Configure(EntityTypeBuilder<PartidaTransitoLibro> builder)
    {
        builder.ToTable("conciliacion_transito_libros", t =>
            t.HasCheckConstraint("ck_conciliacion_transito_libros_tipo", "tipo IN (1, 2)"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Referencia).HasMaxLength(120);
        builder.Property(e => e.Monto).HasPrecision(18, 2);
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Causa).HasMaxLength(300).IsRequired();
        builder.HasIndex(e => new { e.ConciliacionId, e.MovimientoBancarioId }).IsUnique()
            .HasDatabaseName("ux_conciliacion_transito_libros_movimiento");
    }
}

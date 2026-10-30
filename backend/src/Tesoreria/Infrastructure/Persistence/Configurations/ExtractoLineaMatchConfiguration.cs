using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class ExtractoLineaMatchConfiguration : IEntityTypeConfiguration<ExtractoLineaMatch>
{
    public void Configure(EntityTypeBuilder<ExtractoLineaMatch> builder)
    {
        builder.ToTable("extracto_linea_match");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ExtractoLineaId).IsRequired();
        builder.Property(e => e.MovimientoBancarioId).IsRequired();
        builder.Property(e => e.Monto).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.EmparejadoPor).IsRequired();
        builder.Property(e => e.FechaMatch).IsRequired();

        builder.HasIndex(e => e.ExtractoLineaId)
            .HasDatabaseName("ix_extracto_linea_match_linea");

        builder.HasIndex(e => e.MovimientoBancarioId)
            .HasDatabaseName("ix_extracto_linea_match_movimiento");

        builder.HasOne<MovimientoBancario>()
            .WithMany()
            .HasForeignKey(e => e.MovimientoBancarioId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

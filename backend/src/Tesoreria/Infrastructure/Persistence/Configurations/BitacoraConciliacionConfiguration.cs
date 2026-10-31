using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Conciliaciones;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class BitacoraConciliacionConfiguration : IEntityTypeConfiguration<BitacoraConciliacion>
{
    public void Configure(EntityTypeBuilder<BitacoraConciliacion> builder)
    {
        builder.ToTable("bitacora_conciliacion");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ConciliacionId).IsRequired();
        builder.Property(e => e.ExtractoLineaId);
        builder.Property(e => e.MovimientoBancarioId);
        builder.Property(e => e.Accion).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Fecha).IsRequired();

        builder.HasIndex(e => e.ConciliacionId)
            .HasDatabaseName("ix_bitacora_conciliacion_conciliacion");
    }
}

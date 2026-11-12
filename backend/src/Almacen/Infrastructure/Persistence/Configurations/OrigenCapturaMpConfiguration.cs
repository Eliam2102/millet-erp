using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.AppAlmacenes;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>M-335: bitácora de cambios de origen de captura de materia prima en <c>almacen.origen_captura_mp</c>.</summary>
public sealed class OrigenCapturaMpConfiguration : IEntityTypeConfiguration<OrigenCapturaMp>
{
    public void Configure(EntityTypeBuilder<OrigenCapturaMp> builder)
    {
        builder.ToTable("origen_captura_mp", t =>
        {
            t.HasCheckConstraint("ck_origen_captura_mp_origen", "origen IN ('ERP','APP')");
            t.HasCheckConstraint("ck_origen_captura_mp_evento", "evento IN ('recepcion','consumo','merma','requisicion')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Evento).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Origen).HasMaxLength(10).IsRequired();
        builder.Property(x => x.OrigenAnterior).HasMaxLength(10);
        builder.Property(x => x.Motivo).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.Evento, x.CambiadoAt });
    }
}

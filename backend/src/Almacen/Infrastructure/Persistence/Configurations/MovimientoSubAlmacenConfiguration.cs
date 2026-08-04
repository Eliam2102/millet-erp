using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Movimientos;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Almacén-por-línea PR6a: mapea la proyección keyless
/// <see cref="MovimientoSubAlmacen"/> a la vista
/// <c>almacen.v_movimiento_sub_almacen</c>. EF no gestiona la vista (se crea y
/// se dropea en la migración M2 vía SQL crudo); aquí solo se declara el mapeo
/// de lectura.
/// </summary>
public sealed class MovimientoSubAlmacenConfiguration
    : IEntityTypeConfiguration<MovimientoSubAlmacen>
{
    public void Configure(EntityTypeBuilder<MovimientoSubAlmacen> builder)
    {
        builder.HasNoKey();
        builder.ToView("v_movimiento_sub_almacen", "almacen");
        builder.Property(x => x.MovimientoId).HasColumnName("movimiento_id");
        builder.Property(x => x.SubAlmacenId).HasColumnName("sub_almacen_id");
    }
}

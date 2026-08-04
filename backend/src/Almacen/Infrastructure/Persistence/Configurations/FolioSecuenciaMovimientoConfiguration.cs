using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Movimientos;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="FolioSecuenciaMovimiento"/>
/// (F2-PR1). Tabla <c>almacen.folio_secuencias_movimiento</c>.
/// UNIQUE <c>(prefijo, anio)</c>.
/// </summary>
public sealed class FolioSecuenciaMovimientoConfiguration : IEntityTypeConfiguration<FolioSecuenciaMovimiento>
{
    public void Configure(EntityTypeBuilder<FolioSecuenciaMovimiento> builder)
    {
        builder.ToTable("folio_secuencias_movimiento", t =>
        {
            t.HasCheckConstraint("ck_folio_secuencias_ultimo_no_negativo", "ultimo_numero >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Prefijo).HasMaxLength(5).IsRequired();
        builder.Property(x => x.Anio).IsRequired();
        builder.Property(x => x.UltimoNumero).IsRequired();

        builder.HasIndex(x => new { x.Prefijo, x.Anio })
            .IsUnique()
            .HasDatabaseName("ux_folio_secuencias_prefijo_anio");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Cierre;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF de <see cref="PeriodoCerrado"/> (F8-PR2). Tabla
/// <c>almacen.periodos_cerrados</c>. UNIQUE compuesto (empresa, año, mes).
/// </summary>
public sealed class PeriodoCerradoConfiguration : IEntityTypeConfiguration<PeriodoCerrado>
{
    public void Configure(EntityTypeBuilder<PeriodoCerrado> builder)
    {
        builder.ToTable("periodos_cerrados", t =>
        {
            t.HasCheckConstraint("ck_periodos_mes_valido", "mes BETWEEN 1 AND 12");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EmpresaId).IsRequired();
        builder.Property(x => x.Anio).IsRequired();
        builder.Property(x => x.Mes).IsRequired();
        builder.Property(x => x.CerradoAt).IsRequired();
        builder.Property(x => x.CerradoPor).IsRequired();

        builder.HasIndex(x => new { x.EmpresaId, x.Anio, x.Mes })
            .IsUnique()
            .HasDatabaseName("ux_periodos_cerrados");
    }
}

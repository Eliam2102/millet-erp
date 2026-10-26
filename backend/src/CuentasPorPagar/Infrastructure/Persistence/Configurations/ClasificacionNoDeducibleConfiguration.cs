using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Catalogos;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class ClasificacionNoDeducibleConfiguration : IEntityTypeConfiguration<ClasificacionNoDeducible>
{
    public void Configure(EntityTypeBuilder<ClasificacionNoDeducible> builder)
    {
        builder.ToTable("clasificaciones_no_deducibles"); builder.HasKey(c => c.Id); builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Clave).HasMaxLength(40); builder.Property(c => c.Descripcion).HasMaxLength(200);
        builder.HasIndex(c => c.Clave).IsUnique().HasFilter("deleted_at IS NULL");
        var fecha = new DateTimeOffset(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);
        // Catálogo provisional: el real está pendiente de Laura (P8.6).
        var filas = new[] {
            ("COMIDAS", "Comidas"), ("VIATICOS_SIN_CFDI", "Viáticos sin CFDI"), ("PROPINAS", "Propinas"),
            ("ESTACIONAMIENTO", "Estacionamiento"), ("MULTAS_RECARGOS", "Multas y recargos"), ("OTROS", "Otros")
        };
        for (var i = 0; i < filas.Length; i++)
            builder.HasData(new { Id = Guid.Parse($"00000007-000b-0000-0000-{i+1:000000000000}"),
                Clave = filas[i].Item1, Descripcion = filas[i].Item2, Activa = true,
                CreatedAt = fecha, UpdatedAt = fecha, CreatedBy = "seed-P8.6", UpdatedBy = "seed-P8.6", Version = 1 });
    }
}

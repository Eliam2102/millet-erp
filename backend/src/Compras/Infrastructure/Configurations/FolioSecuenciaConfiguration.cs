using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="FolioSecuencia"/> (tabla
/// <c>compras.folio_secuencias</c>, diseño §10.1). PK compuesta por
/// <c>(empresa_id, sucursal_id, anio)</c>. La generación atómica del
/// siguiente folio se hace en F1-PR2 vía <c>UPDATE ... RETURNING</c>.
/// </summary>
public sealed class FolioSecuenciaConfiguration : IEntityTypeConfiguration<FolioSecuencia>
{
    public void Configure(EntityTypeBuilder<FolioSecuencia> builder)
    {
        builder.ToTable("folio_secuencias");

        builder.HasKey(s => new { s.EmpresaId, s.SucursalId, s.Anio });

        builder.Property(s => s.EmpresaId).IsRequired();
        builder.Property(s => s.SucursalId).IsRequired();
        builder.Property(s => s.Anio).IsRequired();
        builder.Property(s => s.Siguiente).IsRequired();
    }
}

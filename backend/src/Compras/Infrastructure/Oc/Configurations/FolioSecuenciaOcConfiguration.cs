using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="FolioSecuenciaOc"/> (tabla
/// <c>compras.folio_secuencias_oc</c>, diseño §10.1). PK compuesta
/// por <c>(empresa_id, sucursal_id, anio)</c>. Sigue el mismo patrón
/// que <c>folio_secuencias</c> de Requisiciones, pero contador
/// independiente. La generación atómica del siguiente folio se hace
/// en F1-PR2 vía <c>UPDATE ... RETURNING siguiente</c>.
/// </summary>
public sealed class FolioSecuenciaOcConfiguration : IEntityTypeConfiguration<FolioSecuenciaOc>
{
    public void Configure(EntityTypeBuilder<FolioSecuenciaOc> builder)
    {
        builder.ToTable("folio_secuencias_oc");

        builder.HasKey(s => new { s.EmpresaId, s.SucursalId, s.Anio });

        builder.Property(s => s.EmpresaId).IsRequired();
        builder.Property(s => s.SucursalId).IsRequired();
        builder.Property(s => s.Anio).IsRequired();
        builder.Property(s => s.Siguiente).HasDefaultValue(1L).IsRequired();
    }
}

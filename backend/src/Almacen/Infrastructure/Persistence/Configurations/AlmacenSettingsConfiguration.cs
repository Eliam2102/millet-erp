using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config de <see cref="AlmacenSettings"/> en <c>almacen.settings</c>.
/// Una fila por empresa con UNIQUE constraint sobre <c>empresa_id</c>.
/// El handler de upsert se apoya en este unique para detectar concurrencia.
/// Molde de <c>ComprasSettingsConfiguration</c>.
/// </summary>
internal sealed class AlmacenSettingsConfiguration : IEntityTypeConfiguration<AlmacenSettings>
{
    public void Configure(EntityTypeBuilder<AlmacenSettings> builder)
    {
        builder.ToTable("settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id);
        builder.Property(s => s.EmpresaId).IsRequired();
        builder.Property(s => s.ReabastoAutomaticoActivo)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(s => s.EmpresaId)
            .IsUnique()
            .HasDatabaseName("ux_almacen_settings_empresa_id");
    }
}

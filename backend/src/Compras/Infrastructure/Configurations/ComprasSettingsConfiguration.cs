using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// EF config de <see cref="ComprasSettings"/> en <c>compras.settings</c>.
/// Una fila por empresa con UNIQUE constraint sobre <c>empresa_id</c>.
/// El handler de upsert se apoya en este unique para detectar concurrencia.
/// </summary>
internal sealed class ComprasSettingsConfiguration : IEntityTypeConfiguration<ComprasSettings>
{
    public void Configure(EntityTypeBuilder<ComprasSettings> builder)
    {
        builder.ToTable("settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id);
        builder.Property(s => s.EmpresaId).IsRequired();
        builder.Property(s => s.AutoGenerarOcAlAutorizar)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(s => s.EmpresaId)
            .IsUnique()
            .HasDatabaseName("ux_settings_empresa_id");
    }
}

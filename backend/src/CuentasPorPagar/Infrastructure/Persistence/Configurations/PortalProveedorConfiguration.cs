using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Portal;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class AccesoPortalProveedorConfiguration : IEntityTypeConfiguration<AccesoPortalProveedor>
{
    public void Configure(EntityTypeBuilder<AccesoPortalProveedor> builder)
    {
        builder.ToTable("acceso_portal_proveedor");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Email).HasMaxLength(254).IsRequired();
        builder.HasIndex(e => e.Email).IsUnique().HasDatabaseName("ux_acceso_portal_proveedor_email");
        builder.HasIndex(e => new { e.EmpresaId, e.ProveedorId });
    }
}

public sealed class CodigoPortalConfiguration : IEntityTypeConfiguration<CodigoPortal>
{
    public void Configure(EntityTypeBuilder<CodigoPortal> builder)
    {
        builder.ToTable("codigo_portal");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CodigoHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => new { e.AccesoId, e.CreatedAt });
    }
}

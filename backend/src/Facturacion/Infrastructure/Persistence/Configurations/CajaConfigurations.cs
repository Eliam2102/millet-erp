using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Cajas;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="Caja"/> y sus hijas (CAJAS-PR1).
/// Tablas <c>facturacion.caja</c> / <c>caja_sucursal</c> / <c>caja_canal</c> /
/// <c>caja_usuario</c>. Las hijas se navegan solo vía el agregado (cascade
/// delete); las FKs a sucursales/canales/usuarios son lógicas (sin FK física
/// cross-schema), validadas por read ports en los handlers.
/// </summary>
public sealed class CajaConfiguration : IEntityTypeConfiguration<Caja>
{
    public void Configure(EntityTypeBuilder<Caja> builder)
    {
        builder.ToTable("caja");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Nombre).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(254);
        builder.Property(e => e.Estatus).HasConversion<short>().IsRequired();

        builder.HasIndex(e => new { e.EmpresaId, e.Nombre })
            .IsUnique()
            .HasDatabaseName("ux_caja_empresa_nombre");

        builder.HasMany(e => e.Sucursales)
            .WithOne()
            .HasForeignKey(s => s.CajaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Canales)
            .WithOne()
            .HasForeignKey(c => c.CajaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Usuarios)
            .WithOne()
            .HasForeignKey(u => u.CajaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Sucursales).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(e => e.Canales).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(e => e.Usuarios).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class CajaSucursalConfiguration : IEntityTypeConfiguration<CajaSucursal>
{
    public void Configure(EntityTypeBuilder<CajaSucursal> builder)
    {
        builder.ToTable("caja_sucursal");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.SucursalId).IsRequired();

        builder.HasIndex(e => new { e.CajaId, e.SucursalId })
            .IsUnique()
            .HasDatabaseName("ux_caja_sucursal");

        // Lookup de Capa A: cajas que cubren una sucursal dada.
        builder.HasIndex(e => e.SucursalId).HasDatabaseName("ix_caja_sucursal_sucursal");
    }
}

public sealed class CajaCanalConfiguration : IEntityTypeConfiguration<CajaCanal>
{
    public void Configure(EntityTypeBuilder<CajaCanal> builder)
    {
        builder.ToTable("caja_canal");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CanalVentaId).IsRequired();

        builder.HasIndex(e => new { e.CajaId, e.CanalVentaId })
            .IsUnique()
            .HasDatabaseName("ux_caja_canal");

        builder.HasIndex(e => e.CanalVentaId).HasDatabaseName("ix_caja_canal_canal");
    }
}

public sealed class CajaUsuarioConfiguration : IEntityTypeConfiguration<CajaUsuario>
{
    public void Configure(EntityTypeBuilder<CajaUsuario> builder)
    {
        builder.ToTable("caja_usuario");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.UsuarioId).IsRequired();

        builder.HasIndex(e => new { e.CajaId, e.UsuarioId })
            .IsUnique()
            .HasDatabaseName("ux_caja_usuario");

        // Lookup de Capa A: cajas de un usuario (evaluador de alcance, PR2).
        builder.HasIndex(e => e.UsuarioId).HasDatabaseName("ix_caja_usuario_usuario");
    }
}

/// <summary>
/// Configuración EF Core de <see cref="UsuarioAlcance"/> (CAJAS-PR1). Tabla
/// <c>facturacion.usuario_alcance</c>. UNIQUE con NULLS NOT DISTINCT: dos
/// concesiones idénticas (incluyendo comodines null) son la misma fila.
/// </summary>
public sealed class UsuarioAlcanceConfiguration : IEntityTypeConfiguration<UsuarioAlcance>
{
    public void Configure(EntityTypeBuilder<UsuarioAlcance> builder)
    {
        builder.ToTable("usuario_alcance");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.UsuarioId).IsRequired();

        builder.HasIndex(e => new { e.EmpresaId, e.UsuarioId, e.SucursalId, e.CanalVentaId })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasDatabaseName("ux_usuario_alcance");

        builder.HasIndex(e => e.UsuarioId).HasDatabaseName("ix_usuario_alcance_usuario");
    }
}

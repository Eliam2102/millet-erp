using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>Configuración EF Core de <see cref="Vehiculo"/> (F8). Tabla <c>facturacion.vehiculo</c>.</summary>
public sealed class VehiculoConfiguration : IEntityTypeConfiguration<Vehiculo>
{
    public void Configure(EntityTypeBuilder<Vehiculo> builder)
    {
        builder.ToTable("vehiculo");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Placa).HasMaxLength(20).IsRequired();
        builder.Property(e => e.ConfigVehicular).HasMaxLength(10).IsRequired();
        builder.Property(e => e.AnioModelo).IsRequired();
        builder.Property(e => e.TipoPermisoSct).HasMaxLength(10);
        builder.Property(e => e.NumPermisoSct).HasMaxLength(50);
        builder.Property(e => e.Aseguradora).HasMaxLength(100);
        builder.Property(e => e.PolizaSeguro).HasMaxLength(50);
        builder.Property(e => e.PesoBrutoVehicular).HasPrecision(10, 3);
        builder.Property(e => e.Activo).IsRequired();

        builder.HasIndex(e => new { e.EmpresaId, e.Placa })
            .IsUnique()
            .HasDatabaseName("ix_vehiculo_placa");
    }
}

/// <summary>Configuración EF Core de <see cref="Operador"/> (F8). Tabla <c>facturacion.operador</c>.</summary>
public sealed class OperadorConfiguration : IEntityTypeConfiguration<Operador>
{
    public void Configure(EntityTypeBuilder<Operador> builder)
    {
        builder.ToTable("operador");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Rfc).HasMaxLength(13).IsRequired();
        builder.Property(e => e.Nombre).HasMaxLength(254).IsRequired();
        builder.Property(e => e.NumLicencia).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Activo).IsRequired();

        builder.HasIndex(e => new { e.EmpresaId, e.Rfc })
            .HasDatabaseName("ix_operador_rfc");
    }
}

/// <summary>
/// Configuración EF Core de <see cref="CartaPorte"/> (F8). Tabla
/// <c>facturacion.carta_porte</c> — subtipo TPT (PK = FK 1:1 a <c>comprobante</c>).
/// </summary>
public sealed class CartaPorteConfiguration : IEntityTypeConfiguration<CartaPorte>
{
    public void Configure(EntityTypeBuilder<CartaPorte> builder)
    {
        builder.ToTable("carta_porte");

        builder.Property(e => e.Origen).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Destino).HasMaxLength(200).IsRequired();
        builder.Property(e => e.OrigenCodigoPostal).HasMaxLength(5);
        builder.Property(e => e.OrigenEstado).HasMaxLength(3);
        builder.Property(e => e.DestinoCodigoPostal).HasMaxLength(5);
        builder.Property(e => e.DestinoEstado).HasMaxLength(3);
        builder.Property(e => e.DistanciaKm).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.VehiculoId).IsRequired();
        builder.Property(e => e.OperadorId).IsRequired();
        builder.Property(e => e.CartaPortePreviaId);
        builder.Property(e => e.PedidoFacturableId);
        builder.Property(e => e.FechaSalida).IsRequired();
        builder.Property(e => e.FechaLlegadaEstimada).IsRequired();

        builder.HasMany(e => e.Mercancias)
            .WithOne()
            .HasForeignKey(m => m.CartaPorteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => e.CartaPortePreviaId)
            .HasDatabaseName("ix_carta_porte_previa")
            .HasFilter("carta_porte_previa_id IS NOT NULL");
    }
}

/// <summary>Configuración EF Core de <see cref="CartaPorteMercancia"/> (F8). Tabla <c>facturacion.carta_porte_mercancia</c>.</summary>
public sealed class CartaPorteMercanciaConfiguration : IEntityTypeConfiguration<CartaPorteMercancia>
{
    public void Configure(EntityTypeBuilder<CartaPorteMercancia> builder)
    {
        builder.ToTable("carta_porte_mercancia");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.CartaPorteId).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(1000).IsRequired();
        builder.Property(e => e.BienesTransp).HasMaxLength(10).IsRequired();
        builder.Property(e => e.ClaveUnidad).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Cantidad).HasPrecision(18, 3).IsRequired();
        builder.Property(e => e.PesoEnKg).HasPrecision(18, 3).IsRequired();
        builder.Property(e => e.MaterialPeligroso).IsRequired();
    }
}

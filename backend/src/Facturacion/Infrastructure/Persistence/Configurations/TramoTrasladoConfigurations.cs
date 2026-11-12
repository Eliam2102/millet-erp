using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>Configuración EF Core de <see cref="TramoTraslado"/> (M-354). Tabla <c>facturacion.tramos_traslado</c>.</summary>
public sealed class TramoTrasladoConfiguration : IEntityTypeConfiguration<TramoTraslado>
{
    public void Configure(EntityTypeBuilder<TramoTraslado> builder)
    {
        builder.ToTable("tramos_traslado");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.PedidoFacturableId).IsRequired();
        builder.Property(e => e.Orden).IsRequired();
        builder.Property(e => e.Modalidad).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Origen).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Destino).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DistanciaKm).HasPrecision(18, 2);
        builder.Property(e => e.OrigenCodigoPostal).HasMaxLength(5);
        builder.Property(e => e.OrigenEstado).HasMaxLength(3);
        builder.Property(e => e.DestinoCodigoPostal).HasMaxLength(5);
        builder.Property(e => e.DestinoEstado).HasMaxLength(3);
        builder.Property(e => e.TerceroNombre).HasMaxLength(254);
        builder.Property(e => e.TerceroRfc).HasMaxLength(13);
        builder.Property(e => e.DocumentoTipo).HasMaxLength(40);
        builder.Property(e => e.DocumentoFolio).HasMaxLength(60);
        builder.Property(e => e.DocumentoUuid).HasMaxLength(36);
        builder.Property(e => e.DocumentoFecha).HasColumnType("date");
        builder.Property(e => e.DocumentoRegistradoPor).HasMaxLength(200);
        builder.Ignore(e => e.TieneDocumentoTercero);

        builder.HasIndex(e => new { e.PedidoFacturableId, e.Orden }).IsUnique().HasDatabaseName("ux_tramos_traslado_pedido_orden");
        builder.HasIndex(e => e.CartaPorteId).HasDatabaseName("ix_tramos_traslado_carta_porte").HasFilter("carta_porte_id IS NOT NULL");
    }
}

/// <summary>Configuración EF Core de <see cref="CorreccionTramo"/> (M-354). Tabla <c>facturacion.tramos_traslado_correcciones</c>.</summary>
public sealed class CorreccionTramoConfiguration : IEntityTypeConfiguration<CorreccionTramo>
{
    public void Configure(EntityTypeBuilder<CorreccionTramo> builder)
    {
        builder.ToTable("tramos_traslado_correcciones");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CartaPorteId).IsRequired();
        builder.Property(e => e.Campo).HasMaxLength(60).IsRequired();
        builder.Property(e => e.ValorAnterior).HasMaxLength(300);
        builder.Property(e => e.ValorNuevo).HasMaxLength(300);
        builder.Property(e => e.Motivo).HasMaxLength(500).IsRequired();
        builder.Property(e => e.UsuarioNombre).HasMaxLength(200);
        builder.Property(e => e.En).IsRequired();

        builder.HasIndex(e => new { e.CartaPorteId, e.En }).HasDatabaseName("ix_tramos_correcciones_carta_porte");
    }
}

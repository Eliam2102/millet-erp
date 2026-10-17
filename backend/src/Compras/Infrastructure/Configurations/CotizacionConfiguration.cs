using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Cotizaciones;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>Configuración EF Core de <see cref="Cotizacion"/> (tabla <c>compras.cotizaciones</c>).</summary>
public sealed class CotizacionConfiguration : IEntityTypeConfiguration<Cotizacion>
{
    public void Configure(EntityTypeBuilder<Cotizacion> builder)
    {
        builder.ToTable("cotizaciones", t =>
        {
            t.HasCheckConstraint("ck_cotizaciones_moneda_iso", "moneda ~ '^[A-Z]{3}$'");
            t.HasCheckConstraint("ck_cotizaciones_vigencia", "vigente_hasta >= fecha_cotizacion");
            t.HasCheckConstraint("ck_cotizaciones_eleccion",
                "(orden_compra_id IS NULL AND motivo_eleccion IS NULL) OR (orden_compra_id IS NOT NULL AND motivo_eleccion IS NOT NULL)");
        });

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(c => c.FolioProveedor).HasMaxLength(40);
        builder.Property(c => c.CondicionesCredito).HasMaxLength(100);
        builder.Property(c => c.MotivoEleccion).HasMaxLength(500);
        builder.Property(c => c.RequisicionId).IsRequired();
        builder.Property(c => c.SucursalId).IsRequired();
        builder.Property(c => c.ProveedorId).IsRequired();
        builder.Property(c => c.FechaCotizacion).IsRequired();
        builder.Property(c => c.VigenteHasta).IsRequired();
        builder.Property(c => c.RegistradaPor).IsRequired();
        builder.Property(c => c.RegistradaEn).IsRequired();

        builder.Ignore(c => c.Elegida);

        // FK interna (mismo esquema). Sin FK a proveedor ni OC (otros esquemas / otro agregado).
        builder.HasOne<Requisicion>().WithMany().HasForeignKey(c => c.RequisicionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.RequisicionId).HasDatabaseName("ix_cotizaciones_requisicion");
        builder.HasIndex(c => c.OrdenCompraId)
            .HasFilter("orden_compra_id IS NOT NULL")
            .HasDatabaseName("ix_cotizaciones_orden_compra");

        builder.HasMany(c => c.Lineas)
            .WithOne()
            .HasForeignKey(l => l.CotizacionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Cotizacion.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>Configuración EF Core de <see cref="LineaCotizacion"/> (tabla <c>compras.cotizacion_lineas</c>).</summary>
public sealed class LineaCotizacionConfiguration : IEntityTypeConfiguration<LineaCotizacion>
{
    public void Configure(EntityTypeBuilder<LineaCotizacion> builder)
    {
        builder.ToTable("cotizacion_lineas", t =>
        {
            t.HasCheckConstraint("ck_cotizacion_lineas_factor", "factor_a_unidad_rq > 0");
            t.HasCheckConstraint("ck_cotizacion_lineas_precio", "precio_unitario > 0");
        });

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.UnidadCotizada).HasMaxLength(10).IsRequired();
        builder.Property(l => l.FactorAUnidadRq).HasColumnType("numeric(18,6)");
        builder.Property(l => l.PrecioUnitario).HasColumnType("numeric(15,4)");

        builder.Ignore(l => l.PrecioPorUnidadRequisicion);

        builder.HasIndex(l => new { l.CotizacionId, l.LineaRequisicionId })
            .IsUnique()
            .HasDatabaseName("ux_cotizacion_lineas_partida");
    }
}

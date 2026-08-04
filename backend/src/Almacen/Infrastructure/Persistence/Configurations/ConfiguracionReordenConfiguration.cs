using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Catalogo;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ConfiguracionReorden"/> (ADR-0047,
/// enmienda 2026-07-06). Tabla <c>almacen.configuraciones_reorden</c>. Clave
/// única compuesta <c>(articulo_id, nivel, entidad_id)</c> — un artículo puede
/// tener varias configs (una por sucursal/almacén). <c>articulo_id</c> y
/// <c>entidad_id</c> son referencias lógicas (sin FK física): la entidad apunta
/// a <c>compartido.sucursales</c> o a <c>almacen.almacenes</c> según el nivel, y
/// su existencia la valida el handler. La exclusión N1⊕N2 por sucursal también
/// vive en el handler (no en un constraint).
/// </summary>
public sealed class ConfiguracionReordenConfiguration
    : IEntityTypeConfiguration<ConfiguracionReorden>
{
    public void Configure(EntityTypeBuilder<ConfiguracionReorden> builder)
    {
        builder.ToTable("configuraciones_reorden", t =>
        {
            t.HasCheckConstraint("ck_config_reorden_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_config_reorden_objetivo", "objetivo BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_config_reorden_nivel", "nivel BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_config_reorden_niveles_no_negativos",
                "minimo >= 0 AND maximo >= 0 AND punto_reorden >= 0");
            t.HasCheckConstraint("ck_config_reorden_max_no_menor_min", "maximo >= minimo");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // Referencias lógicas (otro esquema/entidad según nivel) — sin HasOne.
        builder.Property(x => x.ArticuloId).IsRequired();
        builder.Property(x => x.Nivel).HasConversion<short>().IsRequired();
        builder.Property(x => x.EntidadId).IsRequired();
        builder.Property(x => x.Minimo).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.Maximo).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.PuntoReorden).HasPrecision(14, 4).IsRequired();
        builder.Property(x => x.AutoRequisicion).IsRequired();
        builder.Property(x => x.Objetivo).HasConversion<short>().IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        // Unicidad de una config concreta; NO garantiza la exclusión N1⊕N2 (esa
        // es por sucursal y se valida en el handler).
        builder.HasIndex(x => new { x.ArticuloId, x.Nivel, x.EntidadId })
            .IsUnique()
            .HasDatabaseName("ux_config_reorden_articulo_nivel_entidad");
        // Barrido por artículo (chequeo de exclusión) y por bandera/estatus (motor 5.D).
        builder.HasIndex(x => x.ArticuloId);
        builder.HasIndex(x => new { x.Estatus, x.AutoRequisicion })
            .HasDatabaseName("ix_config_reorden_estatus_auto");
    }
}

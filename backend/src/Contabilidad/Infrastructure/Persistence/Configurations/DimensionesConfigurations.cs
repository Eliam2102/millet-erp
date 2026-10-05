using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary><c>contabilidad.tipos_documento_contable</c> (F1-CON-02, D2). Clave única por empresa (incluye inactivos).</summary>
public sealed class TipoDocumentoContableConfiguration : IEntityTypeConfiguration<TipoDocumentoContable>
{
    public void Configure(EntityTypeBuilder<TipoDocumentoContable> builder)
    {
        builder.ToTable("tipos_documento_contable", t => t.HasCheckConstraint("ck_tipos_documento_estatus", "estatus BETWEEN 0 AND 2"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();
        builder.Ignore(x => x.Activo);
        builder.HasIndex(x => new { x.EmpresaId, x.Clave }).IsUnique().HasDatabaseName("ux_tipos_documento_clave");
    }
}

/// <summary>
/// <c>contabilidad.reglas_dimension</c> (D3/D4). El no-traslape de vigencias lo garantiza el handler bajo un advisory lock
/// (sin <c>btree_gist</c>: no depende de extensiones habilitadas en Flexible Server). El índice único evita duplicar el inicio.
/// </summary>
public sealed class ReglaDimensionConfiguration : IEntityTypeConfiguration<ReglaDimension>
{
    public void Configure(EntityTypeBuilder<ReglaDimension> builder)
    {
        builder.ToTable("reglas_dimension", t =>
        {
            t.HasCheckConstraint("ck_reglas_dimension_dimension", "dimension BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_reglas_dimension_requerimiento", "requerimiento BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_reglas_dimension_vigencia", "vigente_hasta IS NULL OR vigente_hasta >= vigente_desde");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Dimension).HasConversion<short>().IsRequired();
        builder.Property(x => x.Requerimiento).HasConversion<short>().IsRequired();
        builder.Property(x => x.Nota).HasMaxLength(500);

        builder.HasIndex(x => new { x.EmpresaId, x.CuentaId, x.TipoDocumentoId, x.Dimension, x.VigenteDesde })
            .IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_reglas_dimension_inicio");
        builder.HasIndex(x => new { x.EmpresaId, x.VigenteDesde, x.VigenteHasta });

        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TipoDocumentoContable>().WithMany().HasForeignKey(x => x.TipoDocumentoId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>contabilidad.reglas_dimension_uso</c>: una fila por (regla, consumidor, referencia); idempotente.</summary>
public sealed class ReglaDimensionUsoConfiguration : IEntityTypeConfiguration<ReglaDimensionUso>
{
    public void Configure(EntityTypeBuilder<ReglaDimensionUso> builder)
    {
        builder.ToTable("reglas_dimension_uso");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Consumidor).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Referencia).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.EmpresaId, x.ReglaId, x.Consumidor, x.Referencia }).IsUnique().HasDatabaseName("ux_reglas_dimension_uso");
        builder.HasOne<ReglaDimension>().WithMany().HasForeignKey(x => x.ReglaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>contabilidad.centros_costo_sucursal</c> (D6). Sin FK a otros esquemas: se valida por puertos.</summary>
public sealed class CentroCostoSucursalConfiguration : IEntityTypeConfiguration<CentroCostoSucursal>
{
    public void Configure(EntityTypeBuilder<CentroCostoSucursal> builder)
    {
        builder.ToTable("centros_costo_sucursal");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Dim2Id).HasColumnName("dim2_id"); // mismo nombre que en centros_costo (la convención daría dim2id)
        builder.HasIndex(x => new { x.EmpresaId, x.Dim2Id, x.SucursalId }).IsUnique().HasDatabaseName("ux_centros_costo_sucursal");
        builder.HasIndex(x => new { x.EmpresaId, x.SucursalId });
    }
}

/// <summary><c>contabilidad.movimientos_dimension_prueba</c> (D7). Solo inserción; las reglas aplicadas van en jsonb.</summary>
public sealed class MovimientoDimensionPruebaConfiguration : IEntityTypeConfiguration<MovimientoDimensionPrueba>
{
    public void Configure(EntityTypeBuilder<MovimientoDimensionPrueba> builder)
    {
        builder.ToTable("movimientos_dimension_prueba");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CuentaCodigo).HasMaxLength(30).IsRequired();
        builder.Property(x => x.TipoDocumentoClave).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Referencia).HasMaxLength(100);
        builder.Property(x => x.ReglasAplicadas).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Dim1Id).HasColumnName("dim1_id");
        builder.Property(x => x.Dim2Id).HasColumnName("dim2_id");
        builder.Property(x => x.Dim3Id).HasColumnName("dim3_id");
        builder.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.ConfirmadoEn });
        builder.HasIndex(x => new { x.EmpresaId, x.CuentaId });
        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TipoDocumentoContable>().WithMany().HasForeignKey(x => x.TipoDocumentoId).OnDelete(DeleteBehavior.Restrict);
    }
}

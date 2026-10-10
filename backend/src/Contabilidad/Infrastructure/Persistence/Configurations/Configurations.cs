using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>contabilidad.cuentas_contables</c>. Código único por empresa con índice NO parcial
/// (la baja lógica no libera el código, P9). FK self <c>Restrict</c>.
/// </summary>
public sealed class CuentaContableConfiguration : IEntityTypeConfiguration<CuentaContable>
{
    public void Configure(EntityTypeBuilder<CuentaContable> builder)
    {
        builder.ToTable("cuentas_contables", t =>
        {
            t.HasCheckConstraint("ck_cuentas_estatus", "estatus BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_cuentas_nivel", "nivel >= 1");
            t.HasCheckConstraint("ck_cuentas_control_afectable", "cuenta_control = 0 OR COALESCE(tipo, -1) = 1");
            // P24: un rubro no tiene padre, no es colectivo ni pertenece a otro rubro; solo una cuenta raíz pertenece a un rubro.
            t.HasCheckConstraint("ck_cuentas_rubro", "(clase = 0 AND (rubro_id IS NULL OR padre_id IS NULL)) OR (clase = 1 AND padre_id IS NULL AND rubro_id IS NULL AND cuenta_control = 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        builder.Property(x => x.Naturaleza).HasConversion<short>();
        builder.Property(x => x.Tipo).HasConversion<short>();
        builder.Property(x => x.Estatus).HasConversion<short>().IsRequired();
        builder.Property(x => x.CuentaControl).HasConversion<short>().IsRequired();
        builder.Property(x => x.CodigoAgrupador).HasMaxLength(30);
        builder.Property(x => x.GrupoReporte).HasMaxLength(60);
        builder.Property(x => x.Clase).HasConversion<short>().IsRequired();
        builder.Ignore(x => x.Activa);
        builder.Ignore(x => x.EsRubro);
        builder.Ignore(x => x.PendienteValidacion);

        builder.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique().HasDatabaseName("ux_cuentas_contables_codigo");
        builder.HasIndex(x => new { x.EmpresaId, x.PadreId });
        builder.HasIndex(x => new { x.EmpresaId, x.Estatus, x.Tipo });

        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.PadreId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.RubroId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CuentaContableOrigenConfiguration : IEntityTypeConfiguration<CuentaContableOrigen>
{
    public void Configure(EntityTypeBuilder<CuentaContableOrigen> builder)
    {
        builder.ToTable("cuentas_contables_origen");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Fuente).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CodigoOrigen).HasMaxLength(60).IsRequired();
        builder.HasIndex(x => new { x.EmpresaId, x.Fuente, x.CodigoOrigen }).IsUnique().HasDatabaseName("ux_cuentas_origen_fuente_codigo");
        builder.HasIndex(x => x.CuentaId);
        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ImportacionCatalogo>().WithMany().HasForeignKey(x => x.LoteId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ImportacionCatalogoConfiguration : IEntityTypeConfiguration<ImportacionCatalogo>
{
    public void Configure(EntityTypeBuilder<ImportacionCatalogo> builder)
    {
        builder.ToTable("importaciones_catalogo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Fuente).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ArchivoNombre).HasMaxLength(260);
        builder.Property(x => x.HuellaSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AplicadoPor).HasMaxLength(256);
        builder.HasIndex(x => new { x.EmpresaId, x.HuellaSha256 }).IsUnique().HasDatabaseName("ux_importaciones_huella");
    }
}

public sealed class CuentaContableUsoConfiguration : IEntityTypeConfiguration<CuentaContableUso>
{
    public void Configure(EntityTypeBuilder<CuentaContableUso> builder)
    {
        builder.ToTable("cuentas_contables_uso");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Consumidor).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Referencia).HasMaxLength(100);
        builder.HasIndex(x => new { x.EmpresaId, x.CuentaId });
        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SolicitudCatalogoConfiguration : IEntityTypeConfiguration<SolicitudCatalogo>
{
    public void Configure(EntityTypeBuilder<SolicitudCatalogo> builder)
    {
        builder.ToTable("solicitudes_catalogo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Operacion).HasMaxLength(30);
        builder.Property(x => x.Estado).HasMaxLength(20);
        builder.Property(x => x.ComandoJson).HasColumnType("jsonb");
        builder.Property(x => x.CambiosJson).HasColumnType("jsonb");
        builder.Property(x => x.HuellaImportacion).HasMaxLength(64);
        builder.Property(x => x.CodigoAlta).HasMaxLength(30);
        builder.HasIndex(x => new { x.EmpresaId, x.CodigoAlta }).IsUnique()
            .HasFilter("estado = 'Pendiente' AND codigo_alta IS NOT NULL").HasDatabaseName("ux_p9_alta_pendiente");
        builder.HasIndex(x => new { x.EmpresaId, x.HuellaImportacion }).IsUnique()
            .HasFilter("estado = 'Pendiente' AND huella_importacion IS NOT NULL").HasDatabaseName("ux_p9_importacion_pendiente");
        builder.Property(x => x.HuellaCatalogo).HasMaxLength(64);
        builder.Property(x => x.PreparadaPor).HasMaxLength(256);
        builder.Property(x => x.ResueltaPor).HasMaxLength(256);
        builder.Property(x => x.MotivoRechazo).HasMaxLength(1000);
        builder.HasIndex(x => new { x.EmpresaId, x.Estado, x.PreparadaEn });
    }
}

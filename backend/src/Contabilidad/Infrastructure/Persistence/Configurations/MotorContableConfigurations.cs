using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary><c>contabilidad.roles_cuenta</c> (C1.4). Un código puede repetirse con distinto filtro de sucursal/moneda o vigencia.</summary>
public sealed class RolCuentaConfiguration : IEntityTypeConfiguration<RolCuenta>
{
    public void Configure(EntityTypeBuilder<RolCuenta> builder)
    {
        builder.ToTable("roles_cuenta");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Codigo).HasMaxLength(RolCuenta.CodigoMaximo).IsRequired();
        builder.Property(x => x.Descripcion).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Moneda).HasMaxLength(3);
        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.EmpresaId, x.Codigo, x.SucursalId, x.Moneda, x.VigenteDesde })
            .IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_roles_cuenta_codigo_filtro");
    }
}

/// <summary><c>contabilidad.roles_cuenta_cambios</c>: bitácora de cambios de cuenta (C1.4-c).</summary>
public sealed class RolCuentaCambioConfiguration : IEntityTypeConfiguration<RolCuentaCambio>
{
    public void Configure(EntityTypeBuilder<RolCuentaCambio> builder)
    {
        builder.ToTable("roles_cuenta_cambios");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Usuario).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Motivo).HasMaxLength(500);
        builder.HasOne<RolCuenta>().WithMany().HasForeignKey(x => x.RolCuentaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.RolCuentaId, x.CambiadoEn }).HasDatabaseName("ix_roles_cuenta_cambios_rol");
    }
}

/// <summary><c>contabilidad.plantillas_contables</c>: una versión por fila; las líneas en JSON (se siembran por migración).</summary>
public sealed class PlantillaContableConfiguration : IEntityTypeConfiguration<PlantillaContable>
{
    public void Configure(EntityTypeBuilder<PlantillaContable> builder)
    {
        builder.ToTable("plantillas_contables");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TipoEvento).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TipoOperacionClave).HasMaxLength(2).IsRequired();
        builder.Property(x => x.Concepto).HasMaxLength(Poliza.ConceptoMaximo).IsRequired();
        builder.Property(x => x.Descripcion).HasMaxLength(500).IsRequired();
        builder.OwnsMany(x => x.Lineas, b =>
        {
            b.ToJson("lineas");
            b.Property(l => l.Lado).HasConversion<string>();
            b.Property(l => l.Auxiliar).HasConversion<string>();
        });
        builder.Navigation(x => x.Lineas).HasField("_lineas").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(x => new { x.EmpresaId, x.TipoEvento, x.NumeroVersion }).IsUnique().HasDatabaseName("ux_plantillas_contables_version");
    }
}

/// <summary><c>contabilidad.rechazos_contables</c>: un rechazo por evento (tipo + id); el payload se guarda tal cual llegó.</summary>
public sealed class RechazoContableConfiguration : IEntityTypeConfiguration<RechazoContable>
{
    public void Configure(EntityTypeBuilder<RechazoContable> builder)
    {
        builder.ToTable("rechazos_contables");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TipoEvento).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Causa).HasConversion<short>();
        builder.Property(x => x.Estado).HasConversion<short>();
        builder.Property(x => x.Detalle).HasMaxLength(300);
        builder.Property(x => x.Mensaje).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ResueltoPor).HasMaxLength(200);
        builder.Property(x => x.MotivoDescarte).HasMaxLength(500);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.EmpresaId, x.TipoEvento, x.EventoId }).IsUnique().HasDatabaseName("ux_rechazos_contables_evento");
        builder.HasIndex(x => new { x.EmpresaId, x.Estado, x.CreadoEn }).HasDatabaseName("ix_rechazos_contables_bandeja");
    }
}

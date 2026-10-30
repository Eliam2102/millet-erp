using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Corridas;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class ArchivoLayoutPagoConfiguration : IEntityTypeConfiguration<ArchivoLayoutPago>
{
    public void Configure(EntityTypeBuilder<ArchivoLayoutPago> builder)
    {
        builder.ToTable("corrida_pago_archivo");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CorridaId).IsRequired();
        builder.Property(e => e.Banco).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TipoPago).HasConversion<short>().IsRequired();
        builder.Property(e => e.NombreArchivo).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Contenido).IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.LineasValidas).IsRequired();
        builder.Property(e => e.LineasExcluidas).IsRequired();
        builder.Property(e => e.VecesDescargado).IsRequired();
        builder.Property(e => e.GeneradoPor).IsRequired();
        builder.Property(e => e.GeneradoEn).IsRequired();
        builder.Property(e => e.UltimaDescargaEn);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.ArchivoLayoutId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Lineas).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => new { e.EmpresaId, e.CorridaId, e.Banco, e.TipoPago })
            .HasDatabaseName("ux_corrida_pago_archivo_tipo")
            .IsUnique();
    }
}

public sealed class ArchivoLayoutPagoLineaConfiguration : IEntityTypeConfiguration<ArchivoLayoutPagoLinea>
{
    public void Configure(EntityTypeBuilder<ArchivoLayoutPagoLinea> builder)
    {
        builder.ToTable("corrida_pago_archivo_linea");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ArchivoLayoutId).IsRequired();
        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.ProveedorId).IsRequired();
        builder.Property(e => e.ImporteNeto).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.CuentaDestino).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Titular).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Referencia).HasMaxLength(120).IsRequired();
        builder.Property(e => e.Concepto).HasMaxLength(120).IsRequired();

        builder.HasIndex(e => new { e.ArchivoLayoutId, e.FacturaProveedorId })
            .HasDatabaseName("ix_corrida_pago_archivo_linea_factura");
    }
}

public sealed class PerfilLayoutBancoConfiguration : IEntityTypeConfiguration<PerfilLayoutBanco>
{
    public void Configure(EntityTypeBuilder<PerfilLayoutBanco> builder)
    {
        builder.ToTable("corrida_pago_perfil_layout");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Codigo).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Banco).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(e => e.TipoPago).HasConversion<short>().IsRequired();
        builder.Property(e => e.Separador).HasMaxLength(10).IsRequired();
        builder.Property(e => e.IncluyeEncabezado).IsRequired();
        builder.Property(e => e.FormatoImporte).HasMaxLength(30).IsRequired();
        builder.Property(e => e.LongitudCuenta).IsRequired();
        builder.Property(e => e.LongitudTitular).IsRequired();
        builder.Property(e => e.LongitudReferencia).IsRequired();
        builder.Property(e => e.LongitudConcepto).IsRequired();
        builder.Property(e => e.Activo).IsRequired();

        builder.HasIndex(e => new { e.EmpresaId, e.Codigo })
            .HasDatabaseName("ux_corrida_pago_perfil_codigo")
            .IsUnique();
    }
}

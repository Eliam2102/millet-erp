using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>contabilidad.liquidaciones_iva_foto</c> (M-311): foto inmutable de la liquidación del mes. Ninguna columna se actualiza tras el alta
/// (EF lanza si se intenta) y la migración agrega un disparador que rechaza UPDATE y DELETE en la base.
/// </summary>
public sealed class LiquidacionIvaFotoConfiguration : IEntityTypeConfiguration<LiquidacionIvaFoto>
{
    public void Configure(EntityTypeBuilder<LiquidacionIvaFoto> builder)
    {
        builder.ToTable("liquidaciones_iva_foto");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FolioPoliza).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ResumenJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DetalleJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CuadreJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.TratamientosJson).HasColumnType("jsonb").IsRequired();
        foreach (var p in builder.Metadata.GetProperties().Where(p => !p.IsPrimaryKey()))
            p.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.HasIndex(x => new { x.EmpresaId, x.Anio, x.Mes }).HasDatabaseName("ix_liquidaciones_iva_foto_mes");
        builder.HasIndex(x => x.PolizaId).IsUnique().HasDatabaseName("ux_liquidaciones_iva_foto_poliza");
    }
}

public sealed class LiquidacionIvaAcuseConfiguration : IEntityTypeConfiguration<LiquidacionIvaAcuse>
{
    public void Configure(EntityTypeBuilder<LiquidacionIvaAcuse> builder)
    {
        builder.ToTable("liquidaciones_iva_acuse");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Nombre).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TipoContenido).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        foreach (var p in builder.Metadata.GetProperties().Where(p => !p.IsPrimaryKey()))
            p.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Throw);
        builder.HasOne<LiquidacionIvaFoto>().WithMany().HasForeignKey(x => x.FotoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.FotoId).IsUnique().HasDatabaseName("ux_liquidaciones_iva_acuse_foto");
    }
}

public sealed class DiferenciaFiscalIvaConfiguration : IEntityTypeConfiguration<DiferenciaFiscalIva>
{
    public void Configure(EntityTypeBuilder<DiferenciaFiscalIva> builder)
    {
        builder.ToTable("diferencias_fiscales_iva");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Folio).HasMaxLength(60);
        builder.Property(x => x.Uuid).HasMaxLength(40);
        builder.Property(x => x.IvaFoto).HasPrecision(18, 2);
        builder.Property(x => x.IvaActual).HasPrecision(18, 2);
        builder.Property(x => x.Diferencia).HasPrecision(18, 2);
        builder.Property(x => x.Causa).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Estado).HasConversion<short>();
        builder.HasOne<LiquidacionIvaFoto>().WithMany().HasForeignKey(x => x.FotoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.FotoId, x.SaldoIvaDocumentoId }).IsUnique().HasDatabaseName("ux_diferencias_fiscales_iva_foto_saldo");
        builder.HasIndex(x => new { x.EmpresaId, x.Estado }).HasDatabaseName("ix_diferencias_fiscales_iva_estado");
    }
}

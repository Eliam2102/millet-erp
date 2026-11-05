using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Revaluacion;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class RevaluacionCierreConfiguration : IEntityTypeConfiguration<RevaluacionCierre>
{
    public void Configure(EntityTypeBuilder<RevaluacionCierre> builder)
    {
        builder.ToTable("revaluaciones_cierre");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TotalPerdida).HasPrecision(18, 2);
        builder.Property(x => x.TotalUtilidad).HasPrecision(18, 2);
        // Una revaluación por empresa y mes: correrla dos veces no duplica pólizas.
        builder.HasIndex(x => new { x.EmpresaId, x.Anio, x.Mes }).IsUnique().HasDatabaseName("ux_revaluaciones_cierre_empresa_periodo");
        builder.HasIndex(x => x.FechaReversa).HasFilter("reversa_publicada_en IS NULL").HasDatabaseName("ix_revaluaciones_cierre_reversa_pendiente");
        builder.HasMany(x => x.Detalles).WithOne().HasForeignKey(d => d.RevaluacionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Detalles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RevaluacionDetalleConfiguration : IEntityTypeConfiguration<RevaluacionDetalle>
{
    public void Configure(EntityTypeBuilder<RevaluacionDetalle> builder)
    {
        builder.ToTable("revaluaciones_cierre_detalle");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Saldo).HasPrecision(18, 4);
        builder.Property(x => x.TcFactura).HasPrecision(18, 6);
        builder.Property(x => x.TcCierre).HasPrecision(18, 6);
        builder.Property(x => x.ImporteLibros).HasPrecision(18, 2);
        builder.Property(x => x.ImporteCierre).HasPrecision(18, 2);
        builder.Property(x => x.Diferencia).HasPrecision(18, 2);
        builder.HasIndex(x => x.ProveedorId);
    }
}

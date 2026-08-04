using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class ReposicionCajaChicaConfiguration : IEntityTypeConfiguration<ReposicionCajaChica>
{
    public void Configure(EntityTypeBuilder<ReposicionCajaChica> builder)
    {
        builder.ToTable("reposiciones_caja_chica", t =>
        {
            t.HasCheckConstraint("ck_reposicion_monto_positivo", "monto_total > 0");
            t.HasCheckConstraint("ck_reposicion_destino", "destino IN (1, 2)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.Destino).HasConversion<short>().IsRequired();
        builder.Property(e => e.BeneficiarioId).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.MontoTotal).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.NumeroComprobaciones).IsRequired();
        builder.Property(e => e.EsCorteManual).IsRequired();
        builder.Property(e => e.EmitidaPor);
        builder.Property(e => e.FechaEmision).IsRequired();

        builder.HasIndex(e => new { e.SucursalId, e.FechaEmision })
            .HasDatabaseName("ix_reposiciones_sucursal_fecha");
    }
}

public sealed class ConfiguracionReposicionCajaConfiguration
    : IEntityTypeConfiguration<ConfiguracionReposicionCaja>
{
    public void Configure(EntityTypeBuilder<ConfiguracionReposicionCaja> builder)
    {
        builder.ToTable("configuracion_reposicion_caja", t =>
        {
            t.HasCheckConstraint("ck_config_reposicion_minimo_no_negativo", "monto_minimo >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.SucursalId).IsRequired();
        builder.Property(e => e.MontoMinimo).HasPrecision(18, 4).IsRequired();

        builder.HasIndex(e => e.SucursalId)
            .HasDatabaseName("ux_config_reposicion_sucursal")
            .IsUnique();
    }
}

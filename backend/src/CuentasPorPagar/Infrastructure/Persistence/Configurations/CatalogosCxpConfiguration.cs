using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Catalogos;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class AprobadorLimiteConfiguration : IEntityTypeConfiguration<AprobadorLimite>
{
    public void Configure(EntityTypeBuilder<AprobadorLimite> builder)
    {
        builder.ToTable("aprobadores_limites", t =>
        {
            t.HasCheckConstraint("ck_aprobador_monto_positivo", "monto_max > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.EmpleadoId).IsRequired();
        builder.Property(e => e.TipoGasto).HasConversion<short>().IsRequired();
        builder.Property(e => e.MontoMax).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();

        builder.Property(e => e.VigenciaDesde).IsRequired();
        builder.Property(e => e.VigenciaHasta);

        builder.HasIndex(e => new { e.EmpleadoId, e.TipoGasto, e.VigenciaDesde })
            .HasDatabaseName("ix_aprobadores_empleado_tipo");
    }
}

public sealed class PoliticaViaticosConfiguration : IEntityTypeConfiguration<PoliticaViaticos>
{
    public void Configure(EntityTypeBuilder<PoliticaViaticos> builder)
    {
        builder.ToTable("politicas_viaticos", t =>
        {
            t.HasCheckConstraint("ck_politica_monto_positivo", "monto_max_dia > 0");
            t.HasCheckConstraint("ck_politica_dias_positivo", "dias_max > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.PuestoId).IsRequired();
        builder.Property(e => e.TipoDestino).HasConversion<short>().IsRequired();
        builder.Property(e => e.MontoMaxDia).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.DiasMax).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();

        // Una política por (empresa, puesto, tipo_destino)
        builder.HasIndex(e => new { e.EmpresaId, e.PuestoId, e.TipoDestino })
            .HasDatabaseName("ux_politicas_viaticos_puesto_destino")
            .IsUnique();
    }
}

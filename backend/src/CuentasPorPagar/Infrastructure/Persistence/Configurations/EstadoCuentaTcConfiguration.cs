using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class EstadoCuentaTcConfiguration : IEntityTypeConfiguration<EstadoCuentaTc>
{
    public void Configure(EntityTypeBuilder<EstadoCuentaTc> builder)
    {
        builder.ToTable("estados_cuenta_tc", t =>
        {
            t.HasCheckConstraint("ck_ec_periodo_coherente", "periodo_desde <= periodo_hasta");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.TarjetaId).IsRequired();
        builder.Property(e => e.PeriodoDesde).IsRequired();
        builder.Property(e => e.PeriodoHasta).IsRequired();
        builder.Property(e => e.FechaCorte).IsRequired();
        builder.Property(e => e.FechaLimitePago).IsRequired();

        builder.Property(e => e.ArchivoBancoBlobRef).HasMaxLength(400);
        builder.Property(e => e.ArchivoBancoHash).HasMaxLength(64);
        builder.Property(e => e.ArchivoBancoCargadoAt);
        builder.Property(e => e.ArchivoBancoCargadoBy);
        builder.Property(e => e.PerfilParserUsado).HasMaxLength(40);

        builder.Property(e => e.TotalBancoMxn).HasPrecision(14, 2);
        builder.Property(e => e.TotalConciliadoMxn).HasPrecision(14, 2);
        builder.Property(e => e.DiferenciaMxn).HasPrecision(14, 2);
        builder.Property(e => e.DiferenciaCambiariaMxn).HasPrecision(14, 2);

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.FacturaProveedorId);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.EstadoCuentaTcId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(EstadoCuentaTc.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => new { e.TarjetaId, e.PeriodoDesde, e.PeriodoHasta })
            .HasDatabaseName("ux_ec_periodo_tarjeta")
            .IsUnique();

        // SHA-256 único por (tarjeta) — el mismo archivo no se procesa dos veces.
        builder.HasIndex(e => new { e.TarjetaId, e.ArchivoBancoHash })
            .HasDatabaseName("ux_ec_archivo_hash")
            .IsUnique()
            .HasFilter("archivo_banco_hash IS NOT NULL");

        builder.HasIndex(e => new { e.TarjetaId, e.Estado })
            .HasDatabaseName("ix_ec_tarjeta_estado");
    }
}

public sealed class LineaBancoTcConfiguration : IEntityTypeConfiguration<LineaBancoTc>
{
    public void Configure(EntityTypeBuilder<LineaBancoTc> builder)
    {
        builder.ToTable("estado_cuenta_tc_lineas_banco");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.EstadoCuentaTcId).IsRequired();
        builder.Property(e => e.PosicionArchivo).IsRequired();

        builder.Property(e => e.FechaAplicacion).IsRequired();
        builder.Property(e => e.Monto).HasPrecision(14, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.MontoMxn).HasPrecision(14, 2).IsRequired();

        builder.Property(e => e.MerchantRaw).HasMaxLength(200).IsRequired();
        builder.Property(e => e.MerchantNormalizado).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ReferenciaBanco).HasMaxLength(100);
        builder.Property(e => e.TipoSegunBanco).HasMaxLength(40);

        builder.Property(e => e.MovimientoTcId);
        builder.Property(e => e.EstadoMatch).HasConversion<short>().IsRequired();
        builder.Property(e => e.ScoreMatch).HasPrecision(5, 2);

        builder.HasIndex(e => new { e.EstadoCuentaTcId, e.PosicionArchivo })
            .HasDatabaseName("ux_linea_archivo_posicion")
            .IsUnique();

        builder.HasIndex(e => new { e.EstadoCuentaTcId, e.EstadoMatch })
            .HasDatabaseName("ix_linea_estado_match");

        builder.HasIndex(e => e.MovimientoTcId)
            .HasDatabaseName("ix_linea_movimiento")
            .HasFilter("movimiento_tc_id IS NOT NULL");
    }
}

public sealed class PerfilParserBancoConfiguration : IEntityTypeConfiguration<PerfilParserBanco>
{
    public void Configure(EntityTypeBuilder<PerfilParserBanco> builder)
    {
        builder.ToTable("perfiles_parser_banco");
        builder.HasKey(e => e.Codigo);
        builder.Property(e => e.Codigo).HasMaxLength(40).IsRequired();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(e => e.FormatoArchivo).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Encoding).HasMaxLength(40).IsRequired();
        builder.Property(e => e.FilaInicioDatos).IsRequired();
        builder.Property(e => e.ColumnaFecha).HasMaxLength(40).IsRequired();
        builder.Property(e => e.FormatoFecha).HasMaxLength(40).IsRequired();
        builder.Property(e => e.ColumnaMonto).HasMaxLength(40).IsRequired();
        builder.Property(e => e.ColumnaMoneda).HasMaxLength(40);
        builder.Property(e => e.ColumnaMerchant).HasMaxLength(40).IsRequired();
        builder.Property(e => e.ColumnaReferencia).HasMaxLength(40);
        builder.Property(e => e.ColumnaTipo).HasMaxLength(40);
        builder.Property(e => e.ReglaSignoRefund).HasMaxLength(40).IsRequired();
        builder.Property(e => e.LocaleMontos).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Activo).IsRequired();
    }
}

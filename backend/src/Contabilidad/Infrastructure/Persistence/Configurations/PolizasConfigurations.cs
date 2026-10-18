using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>contabilidad.polizas</c> (C1.3). Índices pensados para 25,000 pólizas y 130,000 partidas al mes (R6): la bandeja filtra por
/// periodo, tipo y estado y ordena por fecha; el folio es único por tipo y año (K10.9); la llave de idempotencia y la póliza anulada
/// son únicas (una sola póliza por evento, una sola anulación por póliza: K10.3-b también bajo concurrencia).
/// </summary>
public sealed class PolizaConfiguration : IEntityTypeConfiguration<Poliza>
{
    public void Configure(EntityTypeBuilder<Poliza> builder)
    {
        builder.ToTable("polizas", t =>
        {
            t.HasCheckConstraint("ck_polizas_estado", "estado BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_polizas_origen", "origen BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_polizas_tipo_sat", "tipo_sat BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_polizas_periodo", "periodo_numero BETWEEN 1 AND 13");
            t.HasCheckConstraint("ck_polizas_numero", "(estado = 0 AND numero IS NULL) OR (estado <> 0 AND numero BETWEEN 1 AND 999999)");
            t.HasCheckConstraint("ck_polizas_autorizada_cuadra", "estado = 0 OR (total_cargos = total_abonos AND total_cargos <> 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TipoClave).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TipoSat).HasConversion<short>().IsRequired();
        builder.Property(x => x.Concepto).HasMaxLength(Poliza.ConceptoMaximo).IsRequired();
        builder.Property(x => x.Origen).HasConversion<short>().IsRequired();
        builder.Property(x => x.Estado).HasConversion<short>().IsRequired();
        builder.Property(x => x.OrigenModulo).HasMaxLength(40);
        builder.Property(x => x.OrigenTipo).HasMaxLength(40);
        builder.Property(x => x.OrigenFolio).HasMaxLength(60);
        builder.Property(x => x.OrigenUuid).HasMaxLength(40);
        builder.Property(x => x.LlaveIdempotencia).HasMaxLength(200);
        builder.Property(x => x.MotivoAnulacion).HasMaxLength(Poliza.MotivoMaximo);
        builder.Property(x => x.AnuladaPor).HasMaxLength(200);
        builder.Property(x => x.CapturadaPor).HasMaxLength(200).IsRequired();
        builder.Property(x => x.AutorizadaPor).HasMaxLength(200);
        builder.Property(x => x.TotalCargos).HasPrecision(18, 2);
        builder.Property(x => x.TotalAbonos).HasPrecision(18, 2);
        builder.Ignore(x => x.EsAnulacion);
        builder.Ignore(x => x.Cuadra);
        builder.Ignore(x => x.Folio);

        builder.HasMany(x => x.Partidas).WithOne().HasForeignKey(x => x.PolizaId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Partidas).HasField("_partidas").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<TipoDocumentoContable>().WithMany().HasForeignKey(x => x.TipoOperacionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.EmpresaId, x.TipoOperacionId, x.PeriodoAnio, x.Numero })
            .IsUnique().HasFilter("numero IS NOT NULL").HasDatabaseName("ux_polizas_folio");
        builder.HasIndex(x => new { x.EmpresaId, x.LlaveIdempotencia })
            .IsUnique().HasFilter("llave_idempotencia IS NOT NULL").HasDatabaseName("ux_polizas_llave_idempotencia");
        builder.HasIndex(x => x.PolizaAnuladaId)
            .IsUnique().HasFilter("poliza_anulada_id IS NOT NULL").HasDatabaseName("ux_polizas_anulada");
        builder.HasIndex(x => new { x.EmpresaId, x.PeriodoAnio, x.PeriodoNumero, x.Fecha, x.Id }).HasDatabaseName("ix_polizas_periodo_fecha");
        builder.HasIndex(x => new { x.EmpresaId, x.Estado, x.Fecha }).HasDatabaseName("ix_polizas_estado_fecha");
        builder.HasIndex(x => new { x.EmpresaId, x.OrigenModulo, x.OrigenTipo, x.OrigenDocumentoId })
            .HasFilter("origen_documento_id IS NOT NULL").HasDatabaseName("ix_polizas_documento_origen");
    }
}

/// <summary><c>contabilidad.polizas_partidas</c>. El índice por cuenta sirve a la balanza y a los auxiliares (C1.7).</summary>
public sealed class PolizaPartidaConfiguration : IEntityTypeConfiguration<PolizaPartida>
{
    public void Configure(EntityTypeBuilder<PolizaPartida> builder)
    {
        builder.ToTable("polizas_partidas", t =>
        {
            t.HasCheckConstraint("ck_polizas_partidas_un_lado", "(cargo = 0) <> (abono = 0)");
            t.HasCheckConstraint("ck_polizas_partidas_tipo_cambio", "tipo_cambio > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CuentaCodigo).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Concepto).HasMaxLength(PolizaPartida.ConceptoMaximo);
        builder.Property(x => x.Cargo).HasPrecision(18, 2);
        builder.Property(x => x.Abono).HasPrecision(18, 2);
        builder.Property(x => x.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(x => x.TipoCambio).HasPrecision(18, 6);
        builder.Property(x => x.ImporteOriginal).HasPrecision(18, 2);
        builder.Property(x => x.Proyecto).HasMaxLength(40);

        builder.HasIndex(x => new { x.PolizaId, x.Linea }).IsUnique().HasDatabaseName("ux_polizas_partidas_linea");
        builder.HasIndex(x => new { x.EmpresaId, x.CuentaId }).HasDatabaseName("ix_polizas_partidas_cuenta");
        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>contabilidad.consecutivos_poliza</c>: una fila por empresa, tipo y año (K10.9). Se avanza solo por SQL (UPSERT).</summary>
public sealed class ConsecutivoPolizaConfiguration : IEntityTypeConfiguration<ConsecutivoPoliza>
{
    public void Configure(EntityTypeBuilder<ConsecutivoPoliza> builder)
    {
        builder.ToTable("consecutivos_poliza", t => t.HasCheckConstraint("ck_consecutivos_poliza_numero", "ultimo_numero BETWEEN 1 AND 999999"));
        builder.HasKey(x => new { x.EmpresaId, x.TipoOperacionId, x.Anio });
        builder.HasOne<TipoDocumentoContable>().WithMany().HasForeignKey(x => x.TipoOperacionId).OnDelete(DeleteBehavior.Restrict);
    }
}

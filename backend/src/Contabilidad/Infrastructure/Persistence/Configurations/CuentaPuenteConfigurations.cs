using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary>
/// <c>contabilidad.movimientos_puente</c> (K10.5): auxiliar por OC de la cuenta puente recepción–factura. Una fila por partida y
/// sentido (original o reversa): el índice único hace idempotente la entrada también bajo concurrencia. El reporte filtra por empresa
/// y fecha de corte y agrupa por OC.
/// </summary>
public sealed class MovimientoPuenteConfiguration : IEntityTypeConfiguration<MovimientoPuente>
{
    public void Configure(EntityTypeBuilder<MovimientoPuente> builder)
    {
        builder.ToTable("movimientos_puente", t =>
        {
            t.HasCheckConstraint("ck_movimientos_puente_un_lado", "cargo >= 0 AND abono >= 0 AND (cargo = 0) <> (abono = 0)");
            t.HasCheckConstraint("ck_movimientos_puente_tipo_cambio", "tipo_cambio > 0");
            t.HasCheckConstraint("ck_movimientos_puente_reversa", "(es_reversa AND movimiento_revertido_id IS NOT NULL) OR (NOT es_reversa AND movimiento_revertido_id IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PolizaFolio).HasMaxLength(MovimientoPuente.FolioMaximo);
        builder.Property(x => x.OrdenCompraFolio).HasMaxLength(MovimientoPuente.FolioMaximo);
        builder.Property(x => x.TipoDocumento).HasConversion<short>().IsRequired();
        builder.Property(x => x.Cargo).HasPrecision(18, 2);
        builder.Property(x => x.Abono).HasPrecision(18, 2);
        builder.Property(x => x.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(x => x.TipoCambio).HasPrecision(18, 6);
        builder.Property(x => x.ImporteOriginal).HasPrecision(18, 2);

        builder.HasOne<Poliza>().WithMany().HasForeignKey(x => x.PolizaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CuentaContable>().WithMany().HasForeignKey(x => x.CuentaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MovimientoPuente>().WithMany().HasForeignKey(x => x.MovimientoRevertidoId).OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => new { x.PolizaPartidaId, x.EsReversa }).IsUnique().HasDatabaseName("ux_movimientos_puente_partida");
        builder.HasIndex(x => new { x.EmpresaId, x.OrdenCompraId, x.Fecha }).HasDatabaseName("ix_movimientos_puente_oc_fecha");
        builder.HasIndex(x => new { x.EmpresaId, x.Fecha }).HasDatabaseName("ix_movimientos_puente_fecha");
        builder.HasIndex(x => new { x.EmpresaId, x.TipoDocumento, x.DocumentoId }).HasDatabaseName("ix_movimientos_puente_documento");
        builder.HasIndex(x => x.PolizaId).HasDatabaseName("ix_movimientos_puente_poliza");
    }
}

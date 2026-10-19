using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class DevolucionClienteConfiguration : IEntityTypeConfiguration<DevolucionCliente>
{
    public void Configure(EntityTypeBuilder<DevolucionCliente> builder)
    {
        builder.ToTable("devolucion_cliente", t =>
        {
            t.HasCheckConstraint("ck_devolucion_cliente_estado", "estado IN (1, 2, 3, 4, 5)");
            t.HasCheckConstraint("ck_devolucion_cliente_origen",
                "(origen = 1 AND movimiento_origen_id IS NOT NULL) OR (origen = 2 AND nota_credito_id IS NOT NULL)");
            t.HasCheckConstraint("ck_devolucion_cliente_importe", "importe > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Folio).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.ImporteMaximoOrigen).HasPrecision(18, 2);
        builder.Property(e => e.Importe).HasPrecision(18, 2);
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Motivo).HasMaxLength(400).IsRequired();
        builder.Property(e => e.MotivoRechazo).HasMaxLength(400);
        builder.Property(e => e.MotivoCancelacion).HasMaxLength(400);
        builder.Property(e => e.DestinoBanco).HasMaxLength(80);
        builder.Property(e => e.DestinoClabe).HasMaxLength(18);
        builder.Property(e => e.DestinoTitular).HasMaxLength(200);
        builder.Property(e => e.NotaCreditoFolio).HasMaxLength(40);
        builder.Property(e => e.NotaCreditoUuid).HasMaxLength(40);

        builder.HasOne<MovimientoBancario>()
            .WithMany()
            .HasForeignKey(e => e.MovimientoOrigenId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MovimientoBancario>()
            .WithMany()
            .HasForeignKey(e => e.MovimientoPagoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.EmpresaId, e.Estado }).HasDatabaseName("ix_devolucion_cliente_empresa_estado");
        builder.HasIndex(e => e.MovimientoOrigenId).HasDatabaseName("ix_devolucion_cliente_movimiento_origen");
        // Idempotencia del reembolso de NC: una NC se solicita una sola vez.
        builder.HasIndex(e => e.NotaCreditoId).IsUnique().HasFilter("nota_credito_id IS NOT NULL")
            .HasDatabaseName("ux_devolucion_cliente_nota_credito");

        builder.HasIndex(e => new { e.EmpresaId, e.Folio }).IsUnique().HasDatabaseName("ux_devolucion_cliente_empresa_folio");

        builder.Ignore(e => e.Comprometida);
    }
}

public sealed class FolioSecuenciaDevolucionClienteConfiguration : IEntityTypeConfiguration<FolioSecuenciaDevolucionCliente>
{
    public void Configure(EntityTypeBuilder<FolioSecuenciaDevolucionCliente> builder)
    {
        builder.ToTable("folio_secuencias_devolucion_cliente");
        builder.HasKey(e => e.EmpresaId);
        builder.Property(e => e.EmpresaId).ValueGeneratedNever();
        builder.Property(e => e.Siguiente);
    }
}

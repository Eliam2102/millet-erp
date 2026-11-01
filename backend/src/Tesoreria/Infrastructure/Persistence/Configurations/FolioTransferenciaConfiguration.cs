using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.FoliosTransferencia;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class FolioTransferenciaConfiguration : IEntityTypeConfiguration<FolioTransferencia>
{
    public void Configure(EntityTypeBuilder<FolioTransferencia> builder)
    {
        builder.ToTable("folio_transferencia", t =>
        {
            t.HasCheckConstraint("ck_folio_transferencia_estado", "estado IN (1, 2, 3)");
            t.HasCheckConstraint("ck_folio_transferencia_importe", "importe > 0");
            t.HasCheckConstraint("ck_folio_transferencia_usado",
                "(estado = 2) = (cobro_mostrador_id IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Folio).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Pedido).HasMaxLength(60);
        builder.Property(e => e.Importe).HasPrecision(18, 2);
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.MotivoCancelacion).HasMaxLength(400);

        builder.HasOne<MovimientoBancario>()
            .WithMany()
            .HasForeignKey(e => e.MovimientoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.EmpresaId, e.Folio }).IsUnique().HasDatabaseName("ux_folio_transferencia_empresa_folio");
        builder.HasIndex(e => e.MovimientoId).HasDatabaseName("ix_folio_transferencia_movimiento");
        builder.HasIndex(e => new { e.ClienteId, e.Estado }).HasDatabaseName("ix_folio_transferencia_cliente_estado");
    }
}

public sealed class FolioSecuenciaTransferenciaConfiguration : IEntityTypeConfiguration<FolioSecuenciaTransferencia>
{
    public void Configure(EntityTypeBuilder<FolioSecuenciaTransferencia> builder)
    {
        builder.ToTable("folio_secuencias_transferencia");
        builder.HasKey(e => e.EmpresaId);
        builder.Property(e => e.EmpresaId).ValueGeneratedNever();
        builder.Property(e => e.Siguiente);
    }
}

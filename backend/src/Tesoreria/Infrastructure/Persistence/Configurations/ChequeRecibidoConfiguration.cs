using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Cheques;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class ChequeRecibidoConfiguration : IEntityTypeConfiguration<ChequeRecibido>
{
    public void Configure(EntityTypeBuilder<ChequeRecibido> builder)
    {
        builder.ToTable("cheques_recibidos", t =>
        {
            t.HasCheckConstraint("ck_cheques_recibidos_estado", "estado IN (1, 2, 3)");
            t.HasCheckConstraint("ck_cheques_recibidos_importe", "importe > 0");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.BancoEmisor).HasMaxLength(80).IsRequired();
        builder.Property(e => e.Numero).HasMaxLength(30).IsRequired();
        builder.Property(e => e.Librador).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Importe).HasPrecision(18, 2);
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.MotivoRebote).HasMaxLength(300);
        builder.HasIndex(e => new { e.EmpresaId, e.Estado }).HasDatabaseName("ix_cheques_recibidos_estado");
        // El mismo cheque (banco y número) no se registra dos veces mientras no haya rebotado.
        builder.HasIndex(e => new { e.EmpresaId, e.BancoEmisor, e.Numero }).IsUnique()
            .HasFilter("estado <> 3").HasDatabaseName("ux_cheques_recibidos_vigente");
        builder.HasIndex(e => e.MovimientoId).IsUnique().HasFilter("movimiento_id IS NOT NULL")
            .HasDatabaseName("ux_cheques_recibidos_movimiento");
    }
}

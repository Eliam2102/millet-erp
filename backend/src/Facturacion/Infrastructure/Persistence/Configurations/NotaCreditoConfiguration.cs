using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.NotasCredito;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="NotaCredito"/> (F4-PR2). Tabla
/// <c>facturacion.nota_credito</c> — subtipo TPT cuya PK es FK 1:1 a
/// <c>comprobante</c> (igual que <c>factura_venta</c>/<c>factura_anticipo</c>).
/// </summary>
public sealed class NotaCreditoConfiguration : IEntityTypeConfiguration<NotaCredito>
{
    public void Configure(EntityTypeBuilder<NotaCredito> builder)
    {
        builder.ToTable("nota_credito");

        builder.Property(e => e.Motivo).HasConversion<short>().IsRequired();
        builder.Property(e => e.AnticipoOrigenId);
        builder.Property(e => e.FacturaRelacionadaId);
        builder.Property(e => e.ImporteAfectaInventario).HasPrecision(18, 2).IsRequired();

        builder.Property(e => e.ReembolsoSolicitadoEn);
        builder.Property(e => e.ImporteReembolsoSolicitado).HasPrecision(18, 2);

        // F6.3: reembolso programado al emitir y su estado en Tesorería.
        builder.Property(e => e.ReembolsoAlTimbrar).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.ReembolsoBanco).HasMaxLength(80);
        builder.Property(e => e.ReembolsoClabe).HasMaxLength(18);
        builder.Property(e => e.ReembolsoTitular).HasMaxLength(200);
        builder.Property(e => e.EstadoReembolso).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ReembolsoActualizadoEn);
        builder.Property(e => e.ReembolsoPagadoEl);
        builder.Property(e => e.ReembolsoReferencia).HasMaxLength(100);
        builder.Property(e => e.Comentarios).HasMaxLength(NotaCredito.ComentariosMaxLength);
        builder.Property(e => e.ReembolsoMotivoResolucion).HasMaxLength(400);
        builder.Ignore(e => e.DebeSolicitarReembolsoProgramado);

        // 86e3wjt5r: origen A+W. El índice único es la red de seguridad de la idempotencia por número de nota.
        builder.Property(e => e.AwNumeroNota).HasMaxLength(50);
        builder.Property(e => e.AwNumeroPedido).HasMaxLength(50);
        builder.Property(e => e.AwVendedor).HasMaxLength(120);
        builder.HasIndex(e => e.AwNumeroNota)
            .IsUnique()
            .HasDatabaseName("ux_nota_credito_aw_numero_nota")
            .HasFilter("aw_numero_nota IS NOT NULL");

        builder.Property(e => e.ClaveProdServSat).HasMaxLength(10).IsRequired();
        builder.Property(e => e.ClaveUnidadSat).HasMaxLength(10).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(1000).IsRequired();

        builder.HasIndex(e => e.AnticipoOrigenId)
            .HasDatabaseName("ix_nota_credito_anticipo")
            .HasFilter("anticipo_origen_id IS NOT NULL");

        builder.HasIndex(e => e.FacturaRelacionadaId)
            .HasDatabaseName("ix_nota_credito_factura")
            .HasFilter("factura_relacionada_id IS NOT NULL");
    }
}

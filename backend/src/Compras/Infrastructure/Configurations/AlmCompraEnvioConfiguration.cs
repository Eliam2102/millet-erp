using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.AlmCompra;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// <c>compras.alm_compra_envios</c>: bandeja de salida hacia Almacenes (86e3jvu83). <c>mensaje_id</c> único = idempotencia.
/// La <c>secuencia</c> (identidad) fija el orden estricto por referencia.
/// </summary>
public sealed class AlmCompraEnvioConfiguration : IEntityTypeConfiguration<AlmCompraEnvio>
{
    public void Configure(EntityTypeBuilder<AlmCompraEnvio> builder)
    {
        builder.ToTable("alm_compra_envios", t =>
        {
            t.HasCheckConstraint("ck_alm_compra_envios_tipo", "tipo IN ('COMPRA_RECIBIDA','COMPRA_CANCELADA','COMPRA_MODIFICADA')");
            t.HasCheckConstraint("ck_alm_compra_envios_estado", "estado IN ('PENDIENTE','ENVIADO','PROCESADO','DUPLICADO','ERROR','RECHAZADO')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Secuencia).UseIdentityByDefaultColumn().ValueGeneratedOnAdd();
        builder.Property(x => x.Referencia).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MensajeId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Tipo).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Estado).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Sobre).HasColumnType("text").IsRequired();
        builder.Property(x => x.UltimoError).HasMaxLength(500);
        builder.Property(x => x.CompraIdAlmacenes).HasMaxLength(64);

        builder.HasIndex(x => x.MensajeId).IsUnique().HasDatabaseName("ux_alm_compra_envios_mensaje_id");
        builder.HasIndex(x => new { x.Referencia, x.Secuencia }).HasDatabaseName("ix_alm_compra_envios_referencia_secuencia");
        builder.HasIndex(x => new { x.OrdenCompraId, x.Tipo }).HasDatabaseName("ix_alm_compra_envios_oc_tipo");
        builder.HasIndex(x => x.Estado).HasDatabaseName("ix_alm_compra_envios_estado");
        builder.HasOne<Millet.Compras.Domain.Oc.OrdenCompra>().WithMany()
            .HasForeignKey(x => x.OrdenCompraId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_alm_compra_envios_orden_compra");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Almacen;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class RecepcionOcLocalConfiguration : IEntityTypeConfiguration<RecepcionOcLocal>
{
    public void Configure(EntityTypeBuilder<RecepcionOcLocal> builder)
    {
        builder.ToTable("recepciones_oc_local");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.RecepcionId).IsRequired();
        builder.Property(e => e.FolioRecepcion).HasMaxLength(60).IsRequired();
        builder.Property(e => e.OrdenCompraId).IsRequired();
        builder.Property(e => e.FechaMovimiento).IsRequired();

        builder.Property(e => e.FacturaPendiente).IsRequired();
        builder.Property(e => e.CfdiRecibidoId);
        builder.Property(e => e.Observaciones).HasMaxLength(1000);
        builder.Property(e => e.LineasJson).HasColumnType("jsonb").IsRequired();

        builder.Property(e => e.OcurridoEn).IsRequired();
        builder.Property(e => e.ProyectadoEn).IsRequired();

        builder.HasIndex(e => e.RecepcionId)
            .HasDatabaseName("ux_recepciones_oc_local_recepcion")
            .IsUnique();

        builder.HasIndex(e => e.OrdenCompraId)
            .HasDatabaseName("ix_recepciones_oc_local_oc");

        builder.HasIndex(e => new { e.OrdenCompraId, e.FacturaPendiente })
            .HasDatabaseName("ix_recepciones_oc_local_pendientes")
            .HasFilter("factura_pendiente = true");
    }
}

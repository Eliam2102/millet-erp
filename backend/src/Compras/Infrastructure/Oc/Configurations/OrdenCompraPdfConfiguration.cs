using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="OrdenCompraPdf"/> (F6-PR1,
/// tabla <c>compras.orden_compra_pdf</c>). Relación 1:1 con
/// <c>ordenes_compra</c>: UNIQUE en <c>orden_compra_id</c> + FK con
/// CASCADE para que al borrar la OC se borre el registro de PDF (los
/// blobs huérfanos los limpia el job periódico de F10-PR3).
/// </summary>
public sealed class OrdenCompraPdfConfiguration : IEntityTypeConfiguration<OrdenCompraPdf>
{
    public void Configure(EntityTypeBuilder<OrdenCompraPdf> builder)
    {
        builder.ToTable("orden_compra_pdf", t =>
        {
            t.HasCheckConstraint("ck_oc_pdf_tamano_pos", "tamano_bytes >= 0");
        });

        builder.HasKey(p => p.Id);

        // Id asignado por el dominio con Guid.CreateVersion7(); sin esta
        // declaración, EF Core trata la entity como Modified al insertar
        // dentro de una collection navigation → DbUpdateConcurrencyException.
        // Mismo patrón que LineaOrdenCompra/LineaRequisicion.
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.OrdenCompraId).IsRequired();
        builder.Property(p => p.BlobUrl).HasColumnType("text").IsRequired();
        builder.Property(p => p.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(p => p.TamañoBytes)
            .HasColumnName("tamano_bytes")
            .IsRequired();
        builder.Property(p => p.GeneradoEn).IsRequired();
        builder.Property(p => p.GeneradoPor).HasMaxLength(120).IsRequired();

        builder.HasOne<OrdenCompra>()
            .WithOne()
            .HasForeignKey<OrdenCompraPdf>(p => p.OrdenCompraId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_oc_pdf_orden_compra");

        builder.HasIndex(p => p.OrdenCompraId)
            .IsUnique()
            .HasDatabaseName("ux_oc_pdf_orden_compra");
    }
}

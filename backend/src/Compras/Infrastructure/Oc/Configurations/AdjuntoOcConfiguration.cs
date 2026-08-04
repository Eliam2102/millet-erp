using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="AdjuntoOC"/> (tabla
/// <c>compras.orden_compra_adjuntos</c>, diseño §10.1).
///
/// La FK al agregado raíz con CASCADE la declara
/// <see cref="OrdenCompraConfiguration"/> vía <c>HasMany().WithOne()</c>.
/// Aquí solo se configuran las columnas y la FK al catálogo
/// <c>tipos_documento_oc</c>.
/// </summary>
public sealed class AdjuntoOcConfiguration : IEntityTypeConfiguration<AdjuntoOC>
{
    public void Configure(EntityTypeBuilder<AdjuntoOC> builder)
    {
        builder.ToTable("orden_compra_adjuntos", t =>
        {
            t.HasCheckConstraint("ck_oc_adjuntos_tamano_pos", "tamano_bytes > 0");
        });

        builder.HasKey(a => a.Id);

        // Id asignado por el dominio con Guid.CreateVersion7(); sin esta
        // declaración, EF Core trata la entity como Modified al insertar
        // dentro de una collection navigation → DbUpdateConcurrencyException.
        // Mismo patrón que LineaOrdenCompra/LineaRequisicion.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.OrdenCompraId).IsRequired();
        builder.Property(a => a.TipoDocumentoId).IsRequired();

        builder.Property(a => a.NombreArchivo).HasMaxLength(255).IsRequired();
        builder.Property(a => a.BlobUrl).HasColumnType("text").IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(120).IsRequired();

        builder.Property(a => a.TamañoBytes)
            .HasColumnName("tamano_bytes")
            .IsRequired();

        builder.Property(a => a.FechaCarga).IsRequired();
        builder.Property(a => a.UsuarioCargaId).IsRequired();

        // FK al catálogo. Restrict para no permitir borrar un tipo
        // mientras esté referenciado por adjuntos existentes.
        builder.HasOne<TipoDocumentoOc>()
            .WithMany()
            .HasForeignKey(a => a.TipoDocumentoId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_oc_adjuntos_tipo_documento");

        builder.HasIndex(a => a.OrdenCompraId).HasDatabaseName("ix_oc_adjuntos_oc");
        builder.HasIndex(a => a.TipoDocumentoId).HasDatabaseName("ix_oc_adjuntos_tipo");
    }
}

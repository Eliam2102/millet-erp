using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="TipoDocumentoOc"/> (tabla
/// <c>compras.tipos_documento_oc</c>, diseño §4.12 / §10.1). Incluye
/// seed inicial de 7 tipos del C6 vía <c>HasData</c>:
/// cotizacion, ficha_tecnica (obligatorio si importación),
/// correo_autorizacion, pedimento, factura_proveedor_extranjero,
/// packing_list, otro.
///
/// GUIDs deterministas <c>00000003-0003-0010-0000-...</c> para
/// idempotencia del seed (ADR-0007 — patrón ya usado para permisos
/// canónicos).
/// </summary>
public sealed class TipoDocumentoOcConfiguration : IEntityTypeConfiguration<TipoDocumentoOc>
{
    public void Configure(EntityTypeBuilder<TipoDocumentoOc> builder)
    {
        builder.ToTable("tipos_documento_oc");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Clave).HasMaxLength(40).IsRequired();
        builder.HasIndex(t => t.Clave).IsUnique().HasDatabaseName("uq_tipos_documento_oc_clave");

        builder.Property(t => t.Descripcion).HasMaxLength(200).IsRequired();
        builder.Property(t => t.ObligatorioSiImportacion).HasDefaultValue(false).IsRequired();
        builder.Property(t => t.Activo).HasDefaultValue(true).IsRequired();

        // Seed de 7 tipos del C6 (diseño §4.12).
        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        builder.HasData(
            new
            {
                Id = Guid.Parse("00000003-0003-0010-0000-000000000001"),
                Clave = "cotizacion",
                Descripcion = "Cotización del proveedor",
                ObligatorioSiImportacion = false,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0010-0000-000000000002"),
                Clave = "ficha_tecnica",
                Descripcion = "Ficha técnica del material",
                ObligatorioSiImportacion = true,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0010-0000-000000000003"),
                Clave = "correo_autorizacion",
                Descripcion = "Correo o documento de autorización",
                ObligatorioSiImportacion = false,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0010-0000-000000000004"),
                Clave = "pedimento",
                Descripcion = "Pedimento de importación",
                ObligatorioSiImportacion = false,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0010-0000-000000000005"),
                Clave = "factura_proveedor_extranjero",
                Descripcion = "Factura del proveedor extranjero",
                ObligatorioSiImportacion = false,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0010-0000-000000000006"),
                Clave = "packing_list",
                Descripcion = "Packing list",
                ObligatorioSiImportacion = false,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            },
            new
            {
                Id = Guid.Parse("00000003-0003-0010-0000-000000000007"),
                Clave = "otro",
                Descripcion = "Otro",
                ObligatorioSiImportacion = false,
                Activo = true,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            });
    }
}

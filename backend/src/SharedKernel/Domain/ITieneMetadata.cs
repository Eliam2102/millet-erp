namespace Millet.SharedKernel.Domain;

/// <summary>
/// Metadata transversal (concurrency token + auditoría temporal) para
/// entidades que NO derivan de <see cref="BaseEntity"/> — típicamente
/// catálogos con PK natural no-GUID (p.ej. <c>compartido.canales_venta</c>
/// con PK <c>short</c>). El <c>MetadataSaveChangesInterceptor</c> asigna
/// estos campos en cada <c>SaveChanges</c>, igual que con
/// <see cref="BaseEntity"/>. El <c>IsConcurrencyToken</c> sobre
/// <see cref="Version"/> se configura en la entity configuration del módulo
/// dueño (el BaseDbContext solo lo aplica automático a BaseEntity).
/// </summary>
public interface ITieneMetadata
{
    int Version { get; set; }

    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }

    string? CreatedBy { get; set; }

    string? UpdatedBy { get; set; }
}

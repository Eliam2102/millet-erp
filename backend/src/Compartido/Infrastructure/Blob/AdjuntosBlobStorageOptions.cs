namespace Millet.Compartido.Infrastructure.Blob;

/// <summary>
/// Configuración del almacenamiento del servicio genérico de adjuntos (<c>Adjuntos:BlobStorage</c>).
/// Sin <see cref="ConnectionString"/> (ni la de Compras como respaldo) se usa el filesystem local.
/// </summary>
public sealed class AdjuntosBlobStorageOptions
{
    public const string SectionName = "Adjuntos:BlobStorage";

    /// <summary>Connection string de Azure Storage (Key Vault en QA/Prod). Vacío = usa la de Compras o el stub local.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    public string ContainerName { get; set; } = "adjuntos";

    /// <summary>Directorio raíz del stub local (dev).</summary>
    public string RootPath { get; set; } = Path.Combine(Path.GetTempPath(), "millet-adjuntos-blobs");
}

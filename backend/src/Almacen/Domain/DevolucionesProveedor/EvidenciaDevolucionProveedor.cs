using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.DevolucionesProveedor;

/// <summary>
/// Evidencia adjunta a una devolución a proveedor: foto del material
/// dañado, email de coordinación, orden de salida firmada, etc. La
/// autorización de Dirección requiere al menos una evidencia (mismo
/// patrón que <c>NotaCargo</c> en CxP).
/// </summary>
public sealed class EvidenciaDevolucionProveedor : BaseEntity
{
    public Guid DevolucionId { get; private set; }
    public string TipoEvidencia { get; private set; } = string.Empty;
    public string NombreArchivo { get; private set; } = string.Empty;
    public string BlobRef { get; private set; } = string.Empty;
    public string? Comentario { get; private set; }

    internal EvidenciaDevolucionProveedor() { }

    public EvidenciaDevolucionProveedor(
        Guid id,
        Guid devolucionId,
        string tipoEvidencia,
        string nombreArchivo,
        string blobRef,
        string? comentario = null) : base(id)
    {
        if (devolucionId == Guid.Empty)
            throw new BusinessRuleException("EVIDENCIA_SIN_DEVOLUCION",
                "La evidencia requiere devolución padre.");
        if (string.IsNullOrWhiteSpace(tipoEvidencia) || tipoEvidencia.Length > 50)
            throw new BusinessRuleException("EVIDENCIA_TIPO_INVALIDO",
                "TipoEvidencia requerido (≤50 chars).");
        if (string.IsNullOrWhiteSpace(nombreArchivo) || nombreArchivo.Length > 254)
            throw new BusinessRuleException("EVIDENCIA_NOMBRE_INVALIDO",
                "NombreArchivo requerido (≤254 chars).");
        if (string.IsNullOrWhiteSpace(blobRef) || blobRef.Length > 500)
            throw new BusinessRuleException("EVIDENCIA_BLOB_INVALIDO",
                "BlobRef requerido (≤500 chars).");

        DevolucionId = devolucionId;
        TipoEvidencia = tipoEvidencia;
        NombreArchivo = nombreArchivo;
        BlobRef = blobRef;
        Comentario = comentario;
    }
}

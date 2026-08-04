using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Entidad hija del agregado <see cref="OrdenCompra"/> (diseño §4.11).
/// Modela un documento adjunto: cotización, ficha técnica, correo de
/// autorización, pedimento, etc. El blob real vive en blob storage
/// (Azure Blob en producción, filesystem stub en desarrollo via
/// <see cref="Domain.Ports.Blob.IAlmacenarBlobPort"/>).
///
/// Implementa <see cref="IAuditable"/> + <see cref="IBelongsToAggregate"/>:
/// los cambios en adjuntos (alta y remoción) se registran en
/// <c>core.audit_log</c> agrupados con la OC raíz. NO implementa
/// <see cref="IPerteneceAEmpresa"/> ni <see cref="IFiscalmenteRelevante"/>:
/// el acceso es siempre vía la OC raíz que sí los implementa, y la
/// línea hereda los filters por collection navigation.
///
/// FK física <c>orden_compra_adjuntos → ordenes_compra</c> con CASCADE
/// para integridad referencial.
/// </summary>
public sealed class AdjuntoOC : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid OrdenCompraId { get; private set; }

    /// <summary>
    /// B.2: para que <c>AuditSaveChangesInterceptor</c> agrupe esta fila
    /// con la <see cref="OrdenCompra"/> raíz en <c>core.audit_log</c>.
    /// </summary>
    public Guid AggregateRootId => OrdenCompraId;

    /// <summary>FK a <see cref="TipoDocumentoOc"/>.</summary>
    public Guid TipoDocumentoId { get; private set; }

    public string NombreArchivo { get; private set; } = string.Empty;

    /// <summary>
    /// URL o identificador del blob en el storage backend (ADR-0024).
    /// Formato depende del backend: <c>file:///tmp/oc-blobs/{guid}.ext</c>
    /// para el stub local; <c>https://...blob.core.windows.net/...</c>
    /// para Azure Blob.
    /// </summary>
    public string BlobUrl { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long TamañoBytes { get; private set; }

    public DateTimeOffset FechaCarga { get; private set; }

    public Guid UsuarioCargaId { get; private set; }

    /// <summary>Constructor para EF Core.</summary>
    private AdjuntoOC() { }

    /// <summary>
    /// Construye un adjunto nuevo. Solo invocable desde el agregado raíz
    /// (<see cref="OrdenCompra.AdjuntarDocumento"/>). El caller debe
    /// haber subido el blob al storage antes de invocar; aquí solo se
    /// persiste la metadata.
    /// </summary>
    internal AdjuntoOC(
        Guid id,
        Guid ordenCompraId,
        Guid tipoDocumentoId,
        string nombreArchivo,
        string blobUrl,
        string contentType,
        long tamañoBytes,
        DateTimeOffset fechaCarga,
        Guid usuarioCargaId) : base(id)
    {
        if (ordenCompraId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "ADJUNTO_OC_ID_VACIO",
                "El adjunto debe pertenecer a una OC.");
        }
        if (tipoDocumentoId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TIPO_REQUERIDO",
                "El tipo de documento es requerido.");
        }
        if (usuarioCargaId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "ADJUNTO_USUARIO_REQUERIDO",
                "El usuario de carga es requerido.");
        }
        if (string.IsNullOrWhiteSpace(nombreArchivo) || nombreArchivo.Length > 255)
        {
            throw new BusinessRuleException(
                "ADJUNTO_NOMBRE_INVALIDO",
                "El nombre del archivo debe tener 1-255 caracteres.");
        }
        if (string.IsNullOrWhiteSpace(blobUrl))
        {
            throw new BusinessRuleException(
                "ADJUNTO_BLOB_URL_REQUERIDA",
                "La URL del blob es requerida.");
        }
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 120)
        {
            throw new BusinessRuleException(
                "ADJUNTO_CONTENT_TYPE_INVALIDO",
                "El content type debe tener 1-120 caracteres.");
        }
        if (tamañoBytes <= 0)
        {
            throw new BusinessRuleException(
                "ADJUNTO_TAMANO_INVALIDO",
                "El tamaño del archivo debe ser mayor a cero.");
        }

        OrdenCompraId = ordenCompraId;
        TipoDocumentoId = tipoDocumentoId;
        NombreArchivo = nombreArchivo;
        BlobUrl = blobUrl;
        ContentType = contentType;
        TamañoBytes = tamañoBytes;
        FechaCarga = fechaCarga;
        UsuarioCargaId = usuarioCargaId;
    }
}

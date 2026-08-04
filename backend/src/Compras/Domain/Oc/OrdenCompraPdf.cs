using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// PDF institucional generado al autorizar N2 una OC (F6-PR1). Relación
/// 1:1 con la OC: la PK física es <see cref="OrdenCompraId"/> + UNIQUE.
/// Se regenera si la OC re-autoriza tras un rechazo (PUT semantics).
///
/// <para>
/// El blob real vive en storage (Azure Blob en producción, filesystem
/// stub en desarrollo) y <see cref="BlobUrl"/> apunta a su URL. El
/// endpoint <c>GET /ordenes/{id}/pdf</c> lo descarga vía
/// <c>IAlmacenarBlobPort.ObtenerStreamAsync</c>.
/// </para>
///
/// Implementa <see cref="IAuditable"/> + <see cref="IBelongsToAggregate"/>
/// para que los cambios queden agrupados con la OC raíz.
/// </summary>
public sealed class OrdenCompraPdf : BaseEntity, IAuditable, IBelongsToAggregate
{
    public Guid OrdenCompraId { get; private set; }
    public Guid AggregateRootId => OrdenCompraId;

    public string BlobUrl { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = "application/pdf";
    public long TamañoBytes { get; private set; }
    public DateTimeOffset GeneradoEn { get; private set; }

    /// <summary>
    /// Identificador del generador que produjo el PDF, e.g.
    /// <c>LocalPdfOrdenCompraStub</c> o <c>QuestPdfOrdenCompraImpl</c>.
    /// Útil para audit y para detectar PDFs generados con el stub
    /// pre-F6-PR3 que conviene regenerar.
    /// </summary>
    public string GeneradoPor { get; private set; } = string.Empty;

    private OrdenCompraPdf() { }

    public OrdenCompraPdf(
        Guid id,
        Guid ordenCompraId,
        string blobUrl,
        string contentType,
        long tamañoBytes,
        DateTimeOffset generadoEn,
        string generadoPor) : base(id)
    {
        if (ordenCompraId == Guid.Empty)
        {
            throw new BusinessRuleException(
                "OC_PDF_ORDEN_COMPRA_REQUERIDA",
                "El PDF debe pertenecer a una OC.");
        }
        if (string.IsNullOrWhiteSpace(blobUrl))
        {
            throw new BusinessRuleException(
                "OC_PDF_BLOB_URL_REQUERIDA",
                "La URL del blob es requerida.");
        }
        if (tamañoBytes < 0)
        {
            throw new BusinessRuleException(
                "OC_PDF_TAMAÑO_INVALIDO",
                "El tamaño del PDF no puede ser negativo.");
        }

        OrdenCompraId = ordenCompraId;
        BlobUrl = blobUrl;
        ContentType = contentType;
        TamañoBytes = tamañoBytes;
        GeneradoEn = generadoEn;
        GeneradoPor = generadoPor;
    }
}

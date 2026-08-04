namespace Millet.Compras.Domain.Trazabilidad;

/// <summary>
/// Servicio para construir el árbol de trazabilidad cross-módulo
/// (F7-PR2). Resuelve <c>(tipoDocumento, id)</c> a un <see cref="NodoArbolDocumento"/>
/// con ascendentes + descendentes navegables. V1 solo conoce RQ y OC.
///
/// <para>
/// El servicio vive en <c>Domain</c> para que cualquier módulo lo pueda
/// invocar sin depender de la implementación. CxP, Recepción y Tesorería
/// se conectan implementando providers que el servicio compose
/// (estrategia que se materializa en F8).
/// </para>
/// </summary>
public interface IObtenerArbolDocumentosService
{
    /// <summary>
    /// Construye el árbol completo de trazabilidad desde el documento
    /// indicado. Devuelve <c>null</c> si no se encuentra.
    /// </summary>
    Task<NodoArbolDocumento?> ObtenerAsync(
        TipoDocumentoTrazabilidad tipoDocumento,
        Guid id,
        CancellationToken cancellationToken);
}

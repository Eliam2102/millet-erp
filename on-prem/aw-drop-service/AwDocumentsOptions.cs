namespace Millet.AwDropService;

// ============================================================================
// AwDocumentsOptions
//
// Configuración de las carpetas donde A+W deposita los PDF generados de
// ofertas y pedidos (oferta_<nro>.pdf / pedido_<nro>.pdf, donde <nro> es el
// número de documento A+W = aw_doc_id). El drop service las lee en modo
// SOLO LECTURA y las expone vía GET /documents y GET /documents/{filename}.
//
// El ERP (módulo Millet.Integraciones.Aw) hace PULL de esos endpoints, sube
// el PDF a Blob Storage y publica la URL al Glass Agent. El drop service
// NUNCA borra ni mueve estos archivos: son de A+W.
//
// Bindeada desde la sección `DropService:Documents` de appsettings.json.
// ============================================================================

public sealed class AwDocumentsOptions
{
    public const string SectionName = "DropService:Documents";

    /// <summary>
    /// Carpetas a vigilar. Típicamente dos (quotes para ofertas, orders
    /// para pedidos), pero el <c>doc_type</c> se deriva del prefijo del
    /// nombre del archivo, no de la carpeta — así una carpeta puede
    /// contener ambos tipos sin ambigüedad.
    ///
    /// // PLATFORM-TODO(&lt;AwDocumentsFolders&gt;): rutas placeholder. Las
    /// rutas reales donde A+W exporta los PDF dependen de la instalación en
    /// SER-DATA — pendiente confirmar con equipo A+W de Millet. Ver
    /// `docs/integration/00-system-overview.md` §4.4.
    /// </summary>
    public List<string> Folders { get; init; } = new();
}

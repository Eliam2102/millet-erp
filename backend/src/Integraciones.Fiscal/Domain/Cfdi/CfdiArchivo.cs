using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Integraciones.Fiscal.Domain.Cfdi;

/// <summary>
/// Archivo crudo de un CFDI custodiado en el repositorio del módulo
/// (Decisión 01-B de Facturación, alcance "Separados"). Guarda el XML
/// timbrado + metadata del timbre + PDF estándar opcional.
///
/// <para>
/// <b>Alcance F2-PR1:</b> custodia los CFDIs <b>emitidos</b> por Facturación
/// (que referencia <see cref="Id"/> desde <c>Comprobante.CfdiArchivoId</c>).
/// CxP sigue custodiando sus CFDIs <b>recibidos</b> en su propio blob
/// (<c>CfdiRecibido.XmlBlobRef</c>) — no se unifican físicamente. Un eventual
/// repositorio unificado para conciliación/BI lo arma el módulo 10 jalando de
/// ambos, sin acoplar estos módulos transaccionales.
/// </para>
///
/// <para>Inmutable una vez creado (custodia fiscal): no expone mutadores ni
/// soft-delete. Multi-tenant por <see cref="EmpresaId"/>.</para>
/// </summary>
public sealed class CfdiArchivo : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    /// <summary>UUID fiscal (folio fiscal del SAT). Único por empresa.</summary>
    public string Uuid { get; private set; } = string.Empty;

    /// <summary>XML timbrado completo (CFDI 4.0). Custodia fiscal.</summary>
    public string XmlContenido { get; private set; } = string.Empty;

    /// <summary>PDF estándar (de FiscalAPI) como respaldo de auditoría. Opcional.</summary>
    public byte[]? PdfContenido { get; private set; }

    public string? SelloCfdi { get; private set; }
    public string? SelloSat { get; private set; }
    public string? NoCertificadoSat { get; private set; }
    public string? RfcProveedorCertificacion { get; private set; }

    public DateTimeOffset FechaTimbrado { get; private set; }

    /// <summary>Hash SHA-256 (hex lower) del XML para deduplicación secundaria. Opcional.</summary>
    public string? XmlHashSha256 { get; private set; }

    private CfdiArchivo() { }

    private CfdiArchivo(
        Guid id,
        string uuid,
        string xmlContenido,
        byte[]? pdfContenido,
        string? selloCfdi,
        string? selloSat,
        string? noCertificadoSat,
        string? rfcProveedorCertificacion,
        DateTimeOffset fechaTimbrado,
        string? xmlHashSha256) : base(id)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            throw new BusinessRuleException("CFDI_ARCHIVO_UUID_INVALIDO", "El UUID es obligatorio.");
        if (string.IsNullOrWhiteSpace(xmlContenido))
            throw new BusinessRuleException("CFDI_ARCHIVO_XML_VACIO", "El XML del CFDI es obligatorio.");

        Uuid = uuid;
        XmlContenido = xmlContenido;
        PdfContenido = pdfContenido;
        SelloCfdi = selloCfdi;
        SelloSat = selloSat;
        NoCertificadoSat = noCertificadoSat;
        RfcProveedorCertificacion = rfcProveedorCertificacion;
        FechaTimbrado = fechaTimbrado;
        XmlHashSha256 = xmlHashSha256;
    }

    public static CfdiArchivo Crear(
        string uuid,
        string xmlContenido,
        byte[]? pdfContenido,
        string? selloCfdi,
        string? selloSat,
        string? noCertificadoSat,
        string? rfcProveedorCertificacion,
        DateTimeOffset fechaTimbrado,
        string? xmlHashSha256) =>
        new(
            id: Guid.CreateVersion7(),
            uuid: uuid,
            xmlContenido: xmlContenido,
            pdfContenido: pdfContenido,
            selloCfdi: selloCfdi,
            selloSat: selloSat,
            noCertificadoSat: noCertificadoSat,
            rfcProveedorCertificacion: rfcProveedorCertificacion,
            fechaTimbrado: fechaTimbrado,
            xmlHashSha256: xmlHashSha256);
}

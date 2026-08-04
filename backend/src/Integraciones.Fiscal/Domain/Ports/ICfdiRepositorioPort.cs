namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>
/// Repositorio de archivos de CFDI custodiados por el módulo (Decisión 01-B,
/// alcance "Separados"): guarda/lee el archivo crudo (XML + metadata del timbre
/// + PDF). Lo consume Facturación para sus CFDIs <b>emitidos</b>; el
/// <c>Comprobante</c> referencia el <see cref="CfdiArchivoLeido.Id"/> en vez de
/// duplicar el XML. CxP custodia sus <b>recibidos</b> por su cuenta (no se
/// unifican).
/// </summary>
public interface ICfdiRepositorioPort
{
    /// <summary>Persiste el archivo crudo y devuelve su id. Idempotente por UUID.</summary>
    Task<Guid> GuardarAsync(CfdiArchivoNuevo archivo, CancellationToken cancellationToken);

    /// <summary>Lee un archivo por su id.</summary>
    Task<CfdiArchivoLeido?> ObtenerAsync(Guid cfdiArchivoId, CancellationToken cancellationToken);

    /// <summary>Lee un archivo por su UUID fiscal.</summary>
    Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken cancellationToken);
}

public sealed record CfdiArchivoNuevo(
    string Uuid,
    string XmlContenido,
    byte[]? PdfContenido,
    string? SelloCfdi,
    string? SelloSat,
    string? NoCertificadoSat,
    string? RfcProveedorCertificacion,
    DateTimeOffset FechaTimbrado,
    string? XmlHashSha256 = null);

public sealed record CfdiArchivoLeido(
    Guid Id,
    string Uuid,
    string XmlContenido,
    byte[]? PdfContenido,
    string? SelloSat,
    DateTimeOffset FechaTimbrado);

namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>
/// Puerto de emisión fiscal contra el PAC (dueño: <c>Integraciones.Fiscal</c>,
/// ADR-0038). Facturación construye el <see cref="CfdiEmision"/> desde sus
/// agregados y delega timbrado/cancelación/consulta a este puerto — no
/// implementa el cliente del PAC (D10). FiscalAPI no timbra XML crudo: recibe
/// el modelo estructurado, construye el XML CFDI 4.0, lo sella con el CSD que
/// custodia y lo timbra en una sola operación (D11 — no hay estado
/// <c>Sellado</c> intermedio).
///
/// <para>
/// <b>Asíncrono-tolerante</b> (Decisión 01-C): <see cref="TimbrarAsync"/> puede
/// devolver <see cref="TimbradoEstado.EnProceso"/> ante un resultado ambiguo
/// (timeout / error de red después de enviar); un worker reconcilia los
/// pendientes. No se asume respuesta síncrona del PAC.
/// </para>
///
/// <para>
/// Implementación única: <c>FiscalApiTimbradoAdapter</c> (SDK oficial de
/// FiscalAPI), registrada incondicionalmente por Facturación desde F12-PR3
/// (los stubs de emisión se eliminaron — un ambiente sin credenciales PAC
/// falla visible, nunca con UUIDs falsos).
/// </para>
/// </summary>
public interface ICfdiTimbradoPort
{
    /// <summary>Construye + sella + timbra un CFDI contra el SAT (vía FiscalAPI) en una sola operación.</summary>
    Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken);

    /// <summary>Solicita la cancelación SAT 4.0 de un CFDI timbrado.</summary>
    Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud solicitud, CancellationToken cancellationToken);

    /// <summary>Consulta el estatus de un CFDI ante el SAT (vigente/cancelado).</summary>
    Task<EstatusCfdiResultado> ConsultarEstatusAsync(
        EstatusCfdiSolicitud solicitud,
        CancellationToken cancellationToken);
}

/// <summary>Resultado del timbrado, modelado para tolerar respuesta asíncrona del PAC.</summary>
public enum TimbradoEstado
{
    /// <summary>Timbrado exitoso: UUID + sellos disponibles.</summary>
    Timbrado,

    /// <summary>
    /// Resultado ambiguo: la solicitud pudo haber llegado al PAC pero no hay
    /// confirmación (timeout/red). Un worker la reconcilia por serie+folio.
    /// </summary>
    EnProceso,

    /// <summary>Error definitivo del PAC/SAT (corregible → vuelve a Borrador).</summary>
    Fallido,
}

/// <summary>
/// <paramref name="FolioPac"/> es el folio que el PAC asignó al CFDI —
/// FiscalAPI calcula el atributo <c>Folio</c> internamente (consecutivo
/// por RFC emisor) y rechaza folios del cliente; el folio interno de
/// Millet (serie+consecutivo propio) NO viaja al PAC y se conserva como
/// referencia de control en el comprobante.
/// </summary>
public sealed record TimbradoResultado(
    TimbradoEstado Estado,
    string? Uuid,
    string? SelloCfdi,
    string? SelloSat,
    string? NoCertificadoSat,
    DateTimeOffset? FechaTimbrado,
    string? RfcProveedorCertificacion,
    string? XmlTimbrado,
    string? ErrorCodigo,
    string? ErrorMensaje,
    string? FolioPac = null);

public enum CancelacionEstado
{
    Solicitada,
    EnProceso,
    Aceptada,
    Rechazada,
    Error,
}

/// <summary>
/// <paramref name="EmpresaId"/> resuelve las credenciales PAC por empresa
/// (<c>ConfiguracionPac</c>) — obligatorio porque los workers consultan con
/// bypass de tenancy (sin empresa en el contexto ambiental).
/// </summary>
public sealed record CancelacionSolicitud(
    Guid EmpresaId,
    string Uuid,
    string RfcEmisor,
    string MotivoSat,
    string? UuidSustituto = null);

public sealed record CancelacionResultado(
    CancelacionEstado Estado,
    string? EstatusSat,
    string? ErrorMensaje);

/// <summary>
/// Identificación completa del CFDI ante el servicio de estatus del SAT: el
/// SAT valida uuid + RFCs + total, y la consulta "por valores" de FiscalAPI
/// exige además los últimos 8 caracteres del sello del emisor
/// (<paramref name="Sello8"/> — el comprobante guarda <c>SelloCfdi</c>).
/// </summary>
public sealed record EstatusCfdiSolicitud(
    Guid EmpresaId,
    string Uuid,
    string RfcEmisor,
    string RfcReceptor,
    decimal Total,
    string? Sello8 = null);

public sealed record EstatusCfdiResultado(
    bool EsVigente,
    string EstatusSat,
    bool? EsCancelable,
    string? EstadoCancelacion);

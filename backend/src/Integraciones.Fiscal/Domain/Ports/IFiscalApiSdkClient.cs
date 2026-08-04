namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>
/// Wrapper interno del SDK NuGet oficial <c>Fiscalapi</c>. Encapsula el
/// SDK para que:
/// <list type="bullet">
///   <item>El resto del módulo no dependa directo del SDK (testabilidad).</item>
///   <item>La resolución de credenciales por empresa la haga el adapter,
///   no cada caller (un solo <c>FiscalApiClient</c> del SDK por empresa,
///   cacheado).</item>
///   <item>Errores del SDK se traduzcan a excepciones de dominio
///   consistentes con el resto del módulo.</item>
/// </list>
///
/// <para>
/// <b>Contrato basado en el doc <c>02-flujo-asincrono.md</c> v0.2 §13</b>:
/// el flujo real de FiscalAPI es asíncrono — Rule → Request → Poll →
/// Cosecha — y este puerto refleja eso. Los métodos están organizados
/// por etapa del flujo.
/// </para>
///
/// <para>
/// <b>Ambiente</b>: el adapter siempre apunta a <c>live.fiscalapi.com</c>
/// (ver §13.5 del doc). Sandbox no soporta el flujo completo.
/// </para>
/// </summary>
public interface IFiscalApiSdkClient
{
    // ─── Aprovisionamiento (idempotente, una vez por empresa+RFC) ───────

    /// <summary>
    /// Asegura que existe un <c>Person</c> en FiscalAPI con el RFC dado.
    /// Si no existe, lo crea. Devuelve el ID externo. Idempotente.
    /// </summary>
    Task<PersonExterno> AsegurarPersonAsync(
        Guid empresaId,
        string rfc,
        string legalName,
        string zipCode,
        string satTaxRegimeCode,
        string? satCfdiUseCode,
        string email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sincroniza datos clave del Person en FiscalAPI con los de Millet
    /// (legalName/zipCode/satCfdiUseCode). Necesario porque el SAT valida
    /// el match exacto al timbrar y al simular descarga.
    /// </summary>
    Task SincronizarPersonAsync(
        Guid empresaId,
        string personIdExterno,
        string legalName,
        string zipCode,
        string? satCfdiUseCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sube .cer + .key (FIEL o CSD) al Person dado. FiscalAPI distingue
    /// FIEL vs CSD leyendo el subject del cert, no por flag. Si los
    /// archivos están vacíos o la password es incorrecta, lanza
    /// <see cref="System.InvalidOperationException"/> con el error del PAC.
    /// </summary>
    Task<TaxFilesSubidos> SubirTaxFilesAsync(
        Guid empresaId,
        string personIdExterno,
        string rfc,
        byte[] cerBytes,
        byte[] keyBytes,
        string password,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asegura que existe una DownloadRule en FiscalAPI para la
    /// combinación dada. Si ya existe, devuelve su ID; si no, la crea.
    /// Idempotente.
    /// </summary>
    Task<DownloadRuleExternaDto> AsegurarDownloadRuleAsync(
        Guid empresaId,
        string personIdExterno,
        SatQueryType satQueryType,
        DownloadType downloadType,
        SatInvoiceStatusFilter satInvoiceStatus,
        string descripcion,
        CancellationToken cancellationToken);

    // ─── Operación (1×/día por rule) ────────────────────────────────────

    /// <summary>
    /// Crea una DownloadRequest contra una rule existente. Devuelve el
    /// ID externo + estado inicial. Ventana máxima 31 días (CFDI/Metadata)
    /// o 400 días (Retenciones); el caller valida.
    /// </summary>
    Task<SolicitudDescargaExternaDto> CrearSolicitudAsync(
        Guid empresaId,
        string ruleIdExterno,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken);

    /// <summary>
    /// Consulta el estado actual de una request previamente creada.
    /// Devuelve los IDs de estado externos (SAT + FiscalAPI) para que
    /// <see cref="SolicitudDescarga.AplicarPoll"/> los mapee a la FSM
    /// interna.
    /// </summary>
    Task<SolicitudDescargaExternaDto> ConsultarSolicitudAsync(
        Guid empresaId,
        string requestIdExterno,
        CancellationToken cancellationToken);

    /// <summary>
    /// Itera los meta-items de una request <c>Completada</c>. Cada item es
    /// un resumen del CFDI (UUID + RFCs + fecha + total + estatus SAT).
    /// El estatus (Vigente/Cancelado) y la fecha de cancelación NO vienen
    /// en el XML; por eso seguimos cosechando meta-items aún con rule CFDI
    /// — los pareamos con los XMLs por UUID en el poller.
    /// </summary>
    IAsyncEnumerable<MetaItemDto> ListarMetaItemsAsync(
        Guid empresaId,
        string requestIdExterno,
        CancellationToken cancellationToken);

    /// <summary>
    /// Itera los XMLs CFDI de una request <c>Completada</c> creada con
    /// <c>SatQueryType=CFDI</c>. Cada item incluye el UUID extraído del
    /// timbre fiscal y el contenido del XML decodificado. El caller (el
    /// poller) los emparea con los meta-items por UUID y entrega ambos
    /// al receiver.
    ///
    /// <para>
    /// Para rules <c>Metadata</c> esta iteración queda vacía — FiscalAPI
    /// no genera XMLs en ese caso. Ver doc 02 §13.10.
    /// </para>
    /// </summary>
    IAsyncEnumerable<XmlCfdiItemDto> ListarXmlsAsync(
        Guid empresaId,
        string requestIdExterno,
        CancellationToken cancellationToken);

    // ─── Consulta puntual (refresh por UUID) ────────────────────────────

    /// <summary>
    /// Consulta el estatus SAT actual de un UUID individual. Usa el
    /// endpoint <c>POST /api/v4/invoices/status</c> del SDK
    /// (<c>IInvoiceService.GetStatusAsync</c>). Útil para re-validar
    /// CFDIs <c>PorProcesar</c> antes de pagar.
    /// </summary>
    Task<EstatusUuidDto> ConsultarEstatusUuidAsync(
        Guid empresaId,
        string uuidCfdi,
        string rfcEmisor,
        string rfcReceptor,
        decimal total,
        CancellationToken cancellationToken);

    // ─── Health ─────────────────────────────────────────────────────────

    /// <summary>
    /// Ping autenticado al PAC. Valida que las credenciales que están
    /// configuradas para la empresa funcionan. Tiempo de respuesta + 200
    /// OK = exitoso. Para uso en el botón "Test conexión" del admin UI.
    /// </summary>
    Task<PingResultDto> PingAsync(Guid empresaId, CancellationToken cancellationToken);
}

// ───────────────────────────── DTOs del puerto ─────────────────────────────

public sealed record PersonExterno(
    string IdExterno,
    string Rfc,
    string LegalName,
    string? ZipCode,
    string? SatTaxRegimeId,
    string? SatCfdiUseId);

public sealed record TaxFilesSubidos(
    string CerIdExterno,
    string KeyIdExterno,
    DateTime ValidFrom,
    DateTime ValidTo);

public sealed record DownloadRuleExternaDto(
    string IdExterno,
    string Tin,
    SatQueryType SatQueryType,
    DownloadType DownloadType,
    SatInvoiceStatusFilter SatInvoiceStatus,
    bool IsTest);

public sealed record SolicitudDescargaExternaDto(
    string IdExterno,
    int? SatRequestStatusId,
    int? DownloadRequestStatusId,
    int? InvoiceCount,
    DateTimeOffset? LastAttemptDate,
    DateTimeOffset? NextAttemptDate,
    DateTimeOffset CreatedAt);

/// <summary>
/// Item resumido devuelto por <c>/api/v4/download-requests/{id}/meta-items</c>.
/// Mapeo de <c>Fiscalapi.Models.MetadataItem</c>:
/// <list type="bullet">
///   <item><c>InvoiceUuid</c> → <see cref="Uuid"/></item>
///   <item><c>IssuerTin/Name</c> → <see cref="RfcEmisor"/>/<see cref="NombreEmisor"/></item>
///   <item><c>RecipientTin/Name</c> → <see cref="RfcReceptor"/>/<see cref="NombreReceptor"/></item>
///   <item><c>Status</c> (int 1=Vigente, 0=Cancelado) → <see cref="EstatusSat"/> (string normalizado)</item>
/// </list>
/// </summary>
public sealed record MetaItemDto(
    string Uuid,
    string RfcEmisor,
    string NombreEmisor,
    string? RfcReceptor,
    string? NombreReceptor,
    DateTimeOffset FechaCfdi,
    DateTimeOffset? FechaCertificacionSat,
    decimal Total,
    string TipoComprobante,
    string EstatusSat,
    DateTimeOffset? FechaCancelacion);

/// <summary>
/// XML cosechado de una request <c>CFDI</c> completada. El UUID viene
/// extraído del nodo <c>tfd:TimbreFiscalDigital</c> del XML (el SDK no
/// expone UUID como propiedad del item).
/// </summary>
public sealed record XmlCfdiItemDto(
    string Uuid,
    byte[] XmlBytes);

public sealed record EstatusUuidDto(
    string Uuid,
    string EstatusSat,
    string? EsCancelable,
    string? EstatusCancelacion,
    DateTimeOffset ConsultadoEn);

public sealed record PingResultDto(
    bool Exitosa,
    int StatusCode,
    string Mensaje,
    long TiempoMs,
    DateTimeOffset ConsultadoEn);

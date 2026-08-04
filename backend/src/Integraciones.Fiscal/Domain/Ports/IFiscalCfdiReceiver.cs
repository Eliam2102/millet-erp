namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>
/// Puerto <b>inverso</b> que el consumidor (CxP) implementa para recibir
/// CFDIs cosechados por el <c>DescargaPollerWorker</c>. Cada payload
/// incluye la metadata SAT del meta-item y el XML completo emparejado
/// por UUID (rule <c>SatQueryType=CFDI</c>, doc 02 §13.10).
///
/// <para>
/// <b>Default NoOp</b>: si ningún módulo registra una implementación, el
/// wiring inyecta <c>NoOpFiscalCfdiReceiver</c> que solo loggea. Permite
/// que los workers funcionen end-to-end sin acoplar CxP.
/// </para>
///
/// <para>
/// <b>Idempotencia</b>: el receptor debe ser idempotente — si el UUID
/// ya fue ingestado antes (solicitudes traslapadas, reentregas tras
/// crash), el método debe ser no-op.
/// </para>
/// </summary>
public interface IFiscalCfdiReceiver
{
    /// <summary>
    /// Entrega un CFDI al consumidor. El receptor decide qué hacer:
    /// parsear el XML, persistir en blob, deduplicar, emitir eventos
    /// de dominio, etc.
    ///
    /// <para>
    /// <c>payload.EmpresaId</c> es la empresa Millet a la que pertenece
    /// el CFDI (el worker lo obtiene del <see cref="SolicitudDescarga.EmpresaId"/>).
    /// </para>
    /// </summary>
    Task IngresarCfdiAsync(
        CfdiCosechadoPayload payload,
        CancellationToken cancellationToken);
}

/// <summary>
/// Payload que el <c>DescargaPollerWorker</c> entrega al receiver por
/// cada CFDI cosechado de un <c>DownloadRequest</c> Terminado.
///
/// <para>
/// Combina la metadata SAT del meta-item (estatus Vigente/Cancelado,
/// RFCs, total, etc.) con los bytes del XML que vienen pareados por UUID.
/// </para>
///
/// <para>
/// <b>Edge case sin XML</b>: si el meta-item no tiene XML pareable (raro
/// pero posible si FiscalAPI entrega un meta-item sin el XML
/// correspondiente), el worker logguea warning y NO llama al receiver.
/// El receiver siempre recibe XML válido.
/// </para>
/// </summary>
public sealed record CfdiCosechadoPayload(
    Guid EmpresaId,
    string RfcReceptorMillet,
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
    DateTimeOffset? FechaCancelacion,
    Guid SolicitudDescargaId,
    string RequestIdExterno,
    byte[] XmlBytes);

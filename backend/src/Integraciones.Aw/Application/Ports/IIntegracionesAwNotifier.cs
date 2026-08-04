namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Puerto que abstrae las notificaciones SignalR del módulo Aw para que
/// los workers (que viven en <c>Application.Workers</c>) NO dependan del
/// tipo concreto <c>IntegracionesAwHub</c> (que vive en <c>Api/Hubs/</c>
/// porque necesita las policies de auth de <c>Api/Auth/</c>).
///
/// <para>
/// Implementación productiva: <c>IntegracionesAwHubNotifier</c> en el
/// proyecto Api, usando <c>IHubContext&lt;IntegracionesAwHub&gt;</c>.
/// Implementación para tests: mock que captura las llamadas y permite
/// asertear sin levantar el hub real.
/// </para>
///
/// <para>
/// <b>Naming de eventos:</b> los nombres (<c>"CotizacionEstadoActualizado"</c>,
/// <c>"CorrelacionExitosa"</c>, etc.) son contrato con el frontend
/// (TanStack Router cliente del hub). NO renombrar sin coordinación
/// con el frontend.
/// </para>
///
/// <para>
/// <b>Falla blanda:</b> los workers llaman estos métodos como notificación
/// best-effort después de persistir el cambio en BD. Si la notificación
/// falla (cliente desconectado, hub down), el worker NO debe fallar la
/// operación — la BD ya está en estado correcto y el cliente puede
/// recargar. La implementación debe loguear warning, NO throw.
/// </para>
/// </summary>
public interface IIntegracionesAwNotifier
{
    Task NotifyCorrelacionExitosaAsync(
        Guid empresaId,
        Guid aggregateId,
        string quoteReference,
        long awDocId,
        DateTimeOffset correlatedAt,
        CancellationToken cancellationToken);

    Task NotifyCorrelacionExpiradaAsync(
        Guid empresaId,
        Guid aggregateId,
        string quoteReference,
        DateTimeOffset submittedAt,
        DateTimeOffset expiredAt,
        CancellationToken cancellationToken);

    Task NotifyDropFallidoAsync(
        Guid empresaId,
        Guid aggregateId,
        string quoteReference,
        string errorMessage,
        string errorKind,
        int retryCount,
        CancellationToken cancellationToken);

    Task NotifyDropDeadLetterAsync(
        Guid empresaId,
        Guid aggregateId,
        string quoteReference,
        string errorMessage,
        string errorKind,
        CancellationToken cancellationToken);

    Task NotifyCotizacionEstadoActualizadoAsync(
        Guid empresaId,
        Guid aggregateId,
        string quoteReference,
        string estado,
        DateTimeOffset ocurridoEn,
        CancellationToken cancellationToken);

    /// <summary>
    /// El PDF de A+W (oferta/pedido) quedó disponible. El frontend muestra
    /// el botón/link de descarga que apunta al endpoint proxy
    /// <c>GET /cotizaciones/{id}/pdf</c>.
    /// </summary>
    Task NotifyDocumentoAdjuntadoAsync(
        Guid empresaId,
        Guid aggregateId,
        string quoteReference,
        long awDocId,
        string docType,
        string pdfFilename,
        DateTimeOffset pdfUploadedAt,
        CancellationToken cancellationToken);
}

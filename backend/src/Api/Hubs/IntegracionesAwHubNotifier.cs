using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Ports;

namespace Millet.Api.Hubs;

/// <summary>
/// Implementación productiva de <see cref="IIntegracionesAwNotifier"/>
/// que delega a <see cref="IHubContext{IntegracionesAwHub}"/>. Aísla los
/// workers del módulo Aw de la dependencia directa al hub (que vive en
/// Api/ y depende de Auth/PolicyProvider).
///
/// <para>
/// <b>Falla blanda:</b> si <c>SendAsync</c> tira (cliente desconectado,
/// backplane down), se loguea warning sin re-throw — el worker no debe
/// abortar la operación, la BD ya está actualizada.
/// </para>
/// </summary>
public sealed class IntegracionesAwHubNotifier : IIntegracionesAwNotifier
{
    private readonly IHubContext<IntegracionesAwHub> _hubContext;
    private readonly ILogger<IntegracionesAwHubNotifier> _logger;

    public IntegracionesAwHubNotifier(
        IHubContext<IntegracionesAwHub> hubContext,
        ILogger<IntegracionesAwHubNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task NotifyCorrelacionExitosaAsync(
        Guid empresaId, Guid aggregateId, string quoteReference, long awDocId,
        DateTimeOffset correlatedAt, CancellationToken cancellationToken) =>
        SendAsync(empresaId, "CorrelacionExitosa",
            new CorrelacionExitosaPayload(aggregateId, quoteReference, awDocId, correlatedAt),
            cancellationToken);

    public Task NotifyCorrelacionExpiradaAsync(
        Guid empresaId, Guid aggregateId, string quoteReference,
        DateTimeOffset submittedAt, DateTimeOffset expiredAt,
        CancellationToken cancellationToken) =>
        SendAsync(empresaId, "CorrelacionExpirada",
            new CorrelacionExpiradaPayload(aggregateId, quoteReference, submittedAt, expiredAt),
            cancellationToken);

    public Task NotifyDropFallidoAsync(
        Guid empresaId, Guid aggregateId, string quoteReference,
        string errorMessage, string errorKind, int retryCount,
        CancellationToken cancellationToken) =>
        SendAsync(empresaId, "DropFallido",
            new DropFallidoPayload(aggregateId, quoteReference, errorMessage, errorKind, retryCount),
            cancellationToken);

    public Task NotifyDropDeadLetterAsync(
        Guid empresaId, Guid aggregateId, string quoteReference,
        string errorMessage, string errorKind, CancellationToken cancellationToken) =>
        SendAsync(empresaId, "DropDeadLetter",
            new DropDeadLetterPayload(aggregateId, quoteReference, errorMessage, errorKind),
            cancellationToken);

    public Task NotifyCotizacionEstadoActualizadoAsync(
        Guid empresaId, Guid aggregateId, string quoteReference, string estado,
        DateTimeOffset ocurridoEn, CancellationToken cancellationToken) =>
        SendAsync(empresaId, "CotizacionEstadoActualizado",
            new CotizacionEstadoActualizadoPayload(aggregateId, quoteReference, estado, ocurridoEn),
            cancellationToken);

    public Task NotifyDocumentoAdjuntadoAsync(
        Guid empresaId, Guid aggregateId, string quoteReference, long awDocId,
        string docType, string pdfFilename, DateTimeOffset pdfUploadedAt,
        CancellationToken cancellationToken) =>
        SendAsync(empresaId, "DocumentoAdjuntado",
            new DocumentoAdjuntadoPayload(
                aggregateId, quoteReference, awDocId, docType, pdfFilename, pdfUploadedAt),
            cancellationToken);

    private async Task SendAsync<TPayload>(
        Guid empresaId,
        string eventName,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.Clients
                .Group(IntegracionesAwHub.GroupForEmpresa(empresaId))
                .SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Hub notify {Event} fallo (best-effort, BD ya está actualizada). Empresa={Empresa}.",
                eventName, empresaId);
        }
    }
}

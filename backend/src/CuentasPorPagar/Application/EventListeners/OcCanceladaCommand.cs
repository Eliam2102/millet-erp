using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.EventListeners;

/// <summary>
/// Listener de <c>compras.orden-compra.cancelada.v1</c> (F5-PR1).
/// Detecta facturas en captura asociadas a la OC y loggea alerta para
/// el Auxiliar. PLATFORM-TODO(&lt;NotificacionesCxpOcCancelada&gt;):
/// cuando el módulo Notificaciones esté disponible, emitir notificación
/// directa al equipo de CxP por cada factura afectada.
/// </summary>
public sealed record OcCanceladaCommand(Guid EventId, OcCanceladaPayload Payload) : IRequest;

public sealed class OcCanceladaHandler : IRequestHandler<OcCanceladaCommand>
{
    public const string EventType = "compras.orden-compra.cancelada.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<OcCanceladaHandler> _logger;

    public OcCanceladaHandler(
        CuentasPorPagarDbContext db, IClock clock, ILogger<OcCanceladaHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(OcCanceladaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        // Facturas en estados no terminales asociadas a esta OC.
        var afectadas = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => f.OrdenCompraId == request.Payload.OrdenCompraId
                     && (f.Estado == EstadoPasivo.Capturada || f.Estado == EstadoPasivo.EnRevision))
            .Select(f => new { f.Id, f.FolioProveedor, f.Total })
            .ToListAsync(cancellationToken);

        if (afectadas.Count > 0)
        {
            _logger.LogWarning(
                "[OcCanceladaHandler] OC {OcId} ({Folio}) cancelada — hay {Count} facturas CxP en captura/revisión afectadas. PLATFORM-TODO: notificar al Auxiliar.",
                request.Payload.OrdenCompraId, request.Payload.Folio, afectadas.Count);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: $"OC={request.Payload.OrdenCompraId} Folio={request.Payload.Folio} FacturasAfectadas={afectadas.Count}"));

        await _db.SaveChangesAsync(cancellationToken);
    }
}

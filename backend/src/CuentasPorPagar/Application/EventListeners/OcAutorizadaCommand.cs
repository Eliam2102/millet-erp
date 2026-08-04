using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.EventListeners;

/// <summary>
/// Listener de <c>compras.orden-compra.autorizada.v1</c> (F5-PR1).
/// Por ahora **informativo** — registra que la OC ya es facturable.
/// No proyectamos OCs localmente porque CxP lee on-demand vía
/// <c>IComprasOcReadPort</c> adapter real.
/// </summary>
public sealed record OcAutorizadaCommand(Guid EventId, OcAutorizadaPayload Payload) : IRequest;

public sealed class OcAutorizadaHandler : IRequestHandler<OcAutorizadaCommand>
{
    public const string EventType = "compras.orden-compra.autorizada.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<OcAutorizadaHandler> _logger;

    public OcAutorizadaHandler(
        CuentasPorPagarDbContext db, IClock clock, ILogger<OcAutorizadaHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(OcAutorizadaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: $"OC={request.Payload.OrdenCompraId} Folio={request.Payload.Folio}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[OcAutorizadaHandler] OC {OcId} ({Folio}) autorizada — facturable.",
            request.Payload.OrdenCompraId, request.Payload.Folio);
    }
}

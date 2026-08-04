using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

/// <summary>
/// Listener de <c>facturacion.factura-anticipo.timbrada.v1</c> (CXC-PR3).
/// Los anticipos NO son cartera (levantamiento §0) — el evento se marca
/// procesado como informativo. El estado de cuenta del cliente (CXC-PR6)
/// los consulta on-demand vía <c>IFacturacionAnticiposReadPort</c>, así
/// que no hace falta proyección local.
/// </summary>
public sealed record FacturaAnticipoTimbradaCommand(
    Guid EventId,
    FacturaAnticipoTimbradaPayload Payload) : IRequest;

public sealed class FacturaAnticipoTimbradaHandler : IRequestHandler<FacturaAnticipoTimbradaCommand>
{
    public const string EventType = "facturacion.factura-anticipo.timbrada.v1";

    private readonly CuentasPorCobrarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<FacturaAnticipoTimbradaHandler> _logger;

    public FacturaAnticipoTimbradaHandler(
        CuentasPorCobrarDbContext db,
        IClock clock,
        ILogger<FacturaAnticipoTimbradaHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(FacturaAnticipoTimbradaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        var p = request.Payload;
        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventId, EventType, _clock.UtcNow,
            $"Anticipo={p.AnticipoId} FacturaAnticipo={p.FacturaAnticipoId} ({p.Total} {p.Moneda}) — informativo"));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogDebug(
            "[FacturaAnticipoTimbrada] Anticipo {AnticipoId} registrado como informativo.", p.AnticipoId);
    }
}

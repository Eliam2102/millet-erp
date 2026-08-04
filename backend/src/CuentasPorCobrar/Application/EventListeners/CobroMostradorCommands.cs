using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

// ============================================================================
// Listeners de cobros de mostrador (CXC-PR3). A diferencia del REPP, un
// cobro de mostrador puede pagar comprobantes que NO viven en cartera
// (p.ej. facturas de anticipo — tipo Ingreso igual que la venta). Cuando el
// ComprobanteId no está proyectado, el evento se marca procesado como
// INFORMATIVO en vez de reintentar: reintentar castigaría con DLQ a todos
// los cobros de anticipos. El flujo normal (factura timbrada → cobro) llega
// en orden por el mismo outbox.
// ============================================================================

/// <summary>Listener de <c>facturacion.cobro-mostrador.registrado.v1</c>.</summary>
public sealed record CobroMostradorRegistradoCommand(
    Guid EventId,
    CobroMostradorRegistradoPayload Payload) : IRequest;

public sealed class CobroMostradorRegistradoHandler : IRequestHandler<CobroMostradorRegistradoCommand>
{
    public const string EventType = "facturacion.cobro-mostrador.registrado.v1";

    private readonly CuentasPorCobrarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<CobroMostradorRegistradoHandler> _logger;

    public CobroMostradorRegistradoHandler(
        CuentasPorCobrarDbContext db,
        IClock clock,
        ILogger<CobroMostradorRegistradoHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(CobroMostradorRegistradoCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        var p = request.Payload;
        string detalle;

        var factura = await _db.FacturasCartera
            .FirstOrDefaultAsync(f => f.FacturaVentaId == p.ComprobanteId, cancellationToken);

        if (factura is not null)
        {
            factura.AplicarPago(p.Total);
            _db.MovimientosCartera.Add(new MovimientoCartera(
                empresaId: p.EmpresaId,
                facturaCarteraId: factura.Id,
                tipo: TipoMovimientoCartera.Pago,
                origenComprobanteId: p.CobroMostradorId,
                importe: p.Total,
                fechaMovimiento: p.OcurridoEn));
            detalle = $"Cobro={p.CobroMostradorId} aplicado a factura {factura.Folio} por {p.Total}";
        }
        else
        {
            detalle = $"Cobro={p.CobroMostradorId} sin factura en cartera (Comprobante={p.ComprobanteId}, posible anticipo) — informativo";
            _logger.LogInformation(
                "[CobroMostradorRegistrado] Comprobante {ComprobanteId} no está en cartera — informativo.",
                p.ComprobanteId);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventId, EventType, _clock.UtcNow, detalle));
        await _db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Listener de <c>facturacion.cobro-mostrador.cancelado.v1</c> — reversa por origen.</summary>
public sealed record CobroMostradorCanceladoCommand(
    Guid EventId,
    CobroMostradorCanceladoPayload Payload) : IRequest;

public sealed class CobroMostradorCanceladoHandler : IRequestHandler<CobroMostradorCanceladoCommand>
{
    public const string EventType = "facturacion.cobro-mostrador.cancelado.v1";

    private readonly CuentasPorCobrarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<CobroMostradorCanceladoHandler> _logger;

    public CobroMostradorCanceladoHandler(
        CuentasPorCobrarDbContext db,
        IClock clock,
        ILogger<CobroMostradorCanceladoHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(CobroMostradorCanceladoCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        var p = request.Payload;
        var ahora = _clock.UtcNow;

        var movimientos = await _db.MovimientosCartera
            .Where(m => m.OrigenComprobanteId == p.CobroMostradorId && !m.Revertido)
            .ToListAsync(cancellationToken);

        foreach (var mov in movimientos)
        {
            var factura = await _db.FacturasCartera
                .FirstAsync(f => f.Id == mov.FacturaCarteraId, cancellationToken);
            factura.RevertirPago(mov.Importe);
            mov.Revertir(ahora);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventId, EventType, ahora,
            $"Cobro={p.CobroMostradorId} MovimientosRevertidos={movimientos.Count}"));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[CobroMostradorCancelado] Cobro {CobroId}: {N} movimiento(s) revertido(s).",
            p.CobroMostradorId, movimientos.Count);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

/// <summary>
/// Listener de <c>facturacion.comprobante.cancelado.v1</c> (CXC-PR3) —
/// la reversa de cartera. Por <c>TipoComprobante</c>:
/// <list type="bullet">
///   <item><c>Ingreso</c> — si el comprobante está en cartera, la factura
///   pasa a <c>Cancelada</c> (baja de cobrable); si no (p.ej. factura de
///   anticipo), informativo.</item>
///   <item><c>Pago</c> / <c>Egreso</c> — revierte los movimientos cuyo
///   origen es el comprobante cancelado (REPP → pagos; NC → monto_nc).</item>
///   <item><c>Traslado</c> — informativo (Carta Porte no toca cartera).</item>
/// </list>
/// Sin esta reversa la cartera queda inflada tras cancelaciones
/// (levantamiento §0).
/// </summary>
public sealed record ComprobanteCanceladoCommand(
    Guid EventId,
    ComprobanteCanceladoPayload Payload) : IRequest;

public sealed class ComprobanteCanceladoHandler : IRequestHandler<ComprobanteCanceladoCommand>
{
    public const string EventType = "facturacion.comprobante.cancelado.v1";

    private readonly CuentasPorCobrarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ComprobanteCanceladoHandler> _logger;

    public ComprobanteCanceladoHandler(
        CuentasPorCobrarDbContext db,
        IClock clock,
        ILogger<ComprobanteCanceladoHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(ComprobanteCanceladoCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        var p = request.Payload;
        var ahora = _clock.UtcNow;
        string detalle;

        switch (p.TipoComprobante)
        {
            case "Ingreso":
            {
                var factura = await _db.FacturasCartera
                    .FirstOrDefaultAsync(f => f.FacturaVentaId == p.ComprobanteId, cancellationToken);
                if (factura is not null)
                {
                    factura.Cancelar();
                    detalle = $"Factura {factura.Folio} cancelada en cartera";
                }
                else
                {
                    detalle = $"Ingreso {p.ComprobanteId} no está en cartera (posible anticipo) — informativo";
                }
                break;
            }

            case "Pago":
            case "Egreso":
            {
                var movimientos = await _db.MovimientosCartera
                    .Where(m => m.OrigenComprobanteId == p.ComprobanteId && !m.Revertido)
                    .ToListAsync(cancellationToken);

                foreach (var mov in movimientos)
                {
                    var factura = await _db.FacturasCartera
                        .FirstAsync(f => f.Id == mov.FacturaCarteraId, cancellationToken);
                    if (mov.Tipo == TipoMovimientoCartera.Pago)
                        factura.RevertirPago(mov.Importe);
                    else
                        factura.RevertirNotaCredito(mov.Importe);
                    mov.Revertir(ahora);
                }

                detalle = $"{p.TipoComprobante} {p.ComprobanteId} cancelado: {movimientos.Count} movimiento(s) revertido(s)";
                break;
            }

            default:
                detalle = $"{p.TipoComprobante} {p.ComprobanteId} — informativo (no toca cartera)";
                break;
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventId, EventType, ahora, detalle));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("[ComprobanteCancelado] {Detalle}.", detalle);
    }
}

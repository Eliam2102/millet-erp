using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

/// <summary>
/// Listener de <c>facturacion.nota-credito.timbrada.v1</c> (CXC-PR3).
/// Ajusta el saldo neto 13-K: acumula <c>monto_nc</c> en la factura
/// relacionada y registra el movimiento con origen = NotaCreditoId.
/// NCs sin factura relacionada (caso teórico) quedan como informativas.
/// </summary>
public sealed record NotaCreditoTimbradaCommand(
    Guid EventId,
    NotaCreditoTimbradaPayload Payload) : IRequest;

public sealed class NotaCreditoTimbradaHandler : IRequestHandler<NotaCreditoTimbradaCommand>
{
    public const string EventType = "facturacion.nota-credito.timbrada.v1";

    private readonly CuentasPorCobrarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<NotaCreditoTimbradaHandler> _logger;
    private readonly int _toleranciaDias;

    public NotaCreditoTimbradaHandler(
        CuentasPorCobrarDbContext db,
        IClock clock,
        ILogger<NotaCreditoTimbradaHandler> logger,
        IOptions<AlertasCarteraOptions>? alertas = null)
    {
        _db = db; _clock = clock; _logger = logger;
        _toleranciaDias = Math.Max(0, (alertas?.Value ?? new AlertasCarteraOptions()).ToleranciaDiasVencido);
    }

    public async Task Handle(NotaCreditoTimbradaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup) return;

        var p = request.Payload;
        string detalle;

        if (p.FacturaRelacionadaId is Guid facturaVentaId)
        {
            var factura = await _db.FacturasCartera
                .FirstOrDefaultAsync(f => f.FacturaVentaId == facturaVentaId, cancellationToken)
                ?? throw new EntityNotFoundException("FC_NO_PROYECTADA",
                    $"La factura {facturaVentaId} de la NC {p.NotaCreditoId} no está en cartera todavía — retry.");

            factura.AplicarNotaCredito(p.Total);
            _db.MovimientosCartera.Add(new MovimientoCartera(
                empresaId: p.EmpresaId,
                facturaCarteraId: factura.Id,
                tipo: TipoMovimientoCartera.NotaCredito,
                origenComprobanteId: p.NotaCreditoId,
                importe: p.Total,
                fechaMovimiento: p.OcurridoEn));
            detalle = $"NC={p.NotaCreditoId} ({p.Motivo}) aplicada a factura {factura.Folio} por {p.Total}";
        }
        else
        {
            detalle = $"NC={p.NotaCreditoId} sin factura relacionada — informativa";
            _logger.LogInformation(
                "[NotaCreditoTimbrada] NC {NcId} sin factura relacionada — informativa.", p.NotaCreditoId);
        }

        // C7.5-a: la NC también puede dejar al cliente al corriente.
        if (p.FacturaRelacionadaId is Guid relacionada)
        {
            var facturaNc = await _db.FacturasCartera
                .FirstAsync(f => f.FacturaVentaId == relacionada, cancellationToken);
            await BloqueoPorVencimiento.ReevaluarTrasPagoAsync(
                _db, facturaNc.ClienteId, facturaNc.Moneda, _toleranciaDias,
                DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), _logger, cancellationToken);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventId, EventType, _clock.UtcNow, detalle));
        await _db.SaveChangesAsync(cancellationToken);
    }
}

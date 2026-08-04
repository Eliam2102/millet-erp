using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

/// <summary>
/// Listener de <c>facturacion.recibo-pago.timbrado.v1</c> (CXC-PR3).
/// Aplica el desglose de facturas pagadas del REPP a la proyección
/// <c>factura_cartera</c>, registrando un <see cref="MovimientoCartera"/>
/// por factura con origen = ReciboPagoId (base de la reversa si el REPP
/// se cancela).
///
/// <para>
/// Si alguna factura del desglose aún no está proyectada (entrega fuera
/// de orden), lanza <see cref="EntityNotFoundException"/> — el worker
/// abandona el mensaje y el Service Bus lo reintenta (MaxDeliveryCount=5)
/// dándole tiempo al evento de timbrado de llegar primero.
/// </para>
/// </summary>
public sealed record ReciboPagoTimbradoCommand(
    Guid EventId,
    ReciboPagoTimbradoPayload Payload) : IRequest;

public sealed class ReciboPagoTimbradoHandler : IRequestHandler<ReciboPagoTimbradoCommand>
{
    public const string EventType = "facturacion.recibo-pago.timbrado.v1";

    private readonly CuentasPorCobrarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ReciboPagoTimbradoHandler> _logger;

    public ReciboPagoTimbradoHandler(
        CuentasPorCobrarDbContext db,
        IClock clock,
        ILogger<ReciboPagoTimbradoHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(ReciboPagoTimbradoCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug("[ReciboPagoTimbrado] Evento {EventId} ya procesado — skip.", request.EventId);
            return;
        }

        var p = request.Payload;
        var desglose = p.FacturasPagadas ?? [];
        var aplicadas = 0;

        foreach (var fp in desglose)
        {
            var factura = await _db.FacturasCartera
                .FirstOrDefaultAsync(f => f.FacturaVentaId == fp.FacturaVentaId, cancellationToken)
                ?? throw new EntityNotFoundException("FC_NO_PROYECTADA",
                    $"La factura {fp.FacturaVentaId} del REPP {p.ReciboPagoId} no está en cartera todavía — retry.");

            factura.AplicarPago(fp.ImportePagado);
            _db.MovimientosCartera.Add(new MovimientoCartera(
                empresaId: p.EmpresaId,
                facturaCarteraId: factura.Id,
                tipo: TipoMovimientoCartera.Pago,
                origenComprobanteId: p.ReciboPagoId,
                importe: fp.ImportePagado,
                fechaMovimiento: p.OcurridoEn));
            aplicadas++;
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: _clock.UtcNow,
            detalle: $"Repp={p.ReciboPagoId} FacturasAplicadas={aplicadas} ImporteTotal={p.ImporteTotalPago}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[ReciboPagoTimbrado] REPP {ReppId} aplicado a {N} factura(s) en cartera.",
            p.ReciboPagoId, aplicadas);
    }
}

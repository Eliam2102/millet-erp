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
    private readonly int _toleranciaDias;

    public ReciboPagoTimbradoHandler(
        CuentasPorCobrarDbContext db,
        IClock clock,
        ILogger<ReciboPagoTimbradoHandler> logger,
        IOptions<AlertasCarteraOptions>? alertas = null)
    {
        _db = db; _clock = clock; _logger = logger;
        _toleranciaDias = Math.Max(0, (alertas?.Value ?? new AlertasCarteraOptions()).ToleranciaDiasVencido);
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
        if (p.MovimientoBancarioId is Guid movimientoId)
        {
            // C7.4: con depósitos identificados por CxC varias propuestas pueden llevar el
            // mismo movimiento (una rechazada y su re-propuesta, o el sobrante). El REP
            // solo corresponde a la CONFIRMADA; primero la que aún no tiene REP.
            // Con PropuestaId (C7.4) se correlaciona exacto: el mismo movimiento puede
            // tener varios REP, uno por propuesta.
            var propuesta = await _db.PropuestasAplicacionPago
                .Where(x => x.EmpresaId == p.EmpresaId && x.MovimientoBancarioId == movimientoId
                    && (p.PropuestaId == null || x.Id == p.PropuestaId)
                    && x.Estado == Domain.AplicacionPagos.EstadoPropuestaAplicacion.Confirmada)
                .OrderBy(x => x.ReppTimbrado).ThenBy(x => x.ResueltaEn)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException("PAP_CONFIRMACION_PENDIENTE", "La confirmación de Tesorería todavía no se ha recibido; se reintentará el REP.");
            propuesta.MarcarReppTimbrado(p.ReciboPagoId);
        }
        var desglose = p.FacturasPagadas ?? [];
        var aplicadas = 0;
        var clientesPagados = new HashSet<(Guid?, string)>();

        foreach (var fp in desglose)
        {
            var factura = await _db.FacturasCartera
                .FirstOrDefaultAsync(f => f.FacturaVentaId == fp.FacturaVentaId, cancellationToken)
                ?? throw new EntityNotFoundException("FC_NO_PROYECTADA",
                    $"La factura {fp.FacturaVentaId} del REPP {p.ReciboPagoId} no está en cartera todavía — retry.");

            // FAC-03: un complemento se aplica una sola vez por factura, aunque llegue
            // con otro EventId (índice único ux_movimiento_cartera_origen_factura_tipo).
            if (await _db.MovimientosCartera.AnyAsync(m => m.OrigenComprobanteId == p.ReciboPagoId
                    && m.FacturaCarteraId == factura.Id && m.Tipo == TipoMovimientoCartera.Pago, cancellationToken))
            {
                _logger.LogWarning("[ReciboPagoTimbrado] REPP {ReppId} ya aplicado a la factura {FacturaId} — se ignora.",
                    p.ReciboPagoId, fp.FacturaVentaId);
                continue;
            }

            factura.AplicarPago(fp.ImportePagado);
            clientesPagados.Add((factura.ClienteId, factura.Moneda));
            _db.MovimientosCartera.Add(new MovimientoCartera(
                empresaId: p.EmpresaId,
                facturaCarteraId: factura.Id,
                tipo: TipoMovimientoCartera.Pago,
                origenComprobanteId: p.ReciboPagoId,
                importe: fp.ImportePagado,
                fechaMovimiento: p.OcurridoEn));
            aplicadas++;
        }

        // C7.5-a: desbloqueo automático por cliente y moneda pagados.
        foreach (var (clienteId, moneda) in clientesPagados)
            await BloqueoPorVencimiento.ReevaluarTrasPagoAsync(
                _db, clienteId, moneda, _toleranciaDias,
                DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime), _logger, cancellationToken);

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

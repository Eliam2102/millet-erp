using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Listener in-proc (F5-PR2) que consume una devolución de material y
/// ajusta el acumulado recibido. Si la OC estaba <c>Cerrada</c> y el
/// recálculo deja alguna dimensión fuera del cierre, el agregado
/// transiciona de regreso a <c>Autorizada</c> y emite
/// <see cref="OrdenCompraReabriertaEvent"/>.
/// </summary>
public sealed class OcDevolucionRegistradaListener
    : INotificationHandler<OcDevolucionRegistradaEvent>
{
    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<OcDevolucionRegistradaListener> _logger;

    public OcDevolucionRegistradaListener(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<OcDevolucionRegistradaListener> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(OcDevolucionRegistradaEvent notification, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == notification.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{notification.OrdenCompraId}' al registrar devolución.");

        var resultado = oc.RegistrarRecepcionLinea(
            notification.LineaOrdenCompraId,
            notification.CantidadAcumuladaAjustada,
            notification.OcurridoEn);

        if (resultado.Reabrierta is { } reabierta)
        {
            await _publisher.Publish(reabierta, cancellationToken);
        }

        await AjustarRequisicionAsync(oc, notification.LineaOrdenCompraId, cancellationToken);

        // ADR-0009: incluir cierre/reapertura en la transacción del cambio.
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Devolución aplicada. OcId={OcId} LineaId={LineaId} CantidadAjustada={Cantidad} Reabierta={Reabierta}",
            notification.OrdenCompraId,
            notification.LineaOrdenCompraId,
            notification.CantidadAcumuladaAjustada,
            resultado.Reabrierta is not null);
    }

    /// <summary>
    /// F-01: la línea de RQ baja al recibido neto de sus líneas de OC en la misma
    /// transacción. Best-effort: si la RQ no lo admite (p. ej., ya no está
    /// EnSurtido) se registra y la OC se ajusta igual; el dominio valida antes de
    /// mutar, así que no queda nada a medias.
    /// </summary>
    private async Task AjustarRequisicionAsync(
        Domain.Oc.OrdenCompra oc, Guid lineaOcId, CancellationToken cancellationToken)
    {
        var lineaOc = oc.Lineas.FirstOrDefault(l => l.Id == lineaOcId);
        if (lineaOc is not { RequisicionId: { } rqId, LineaRequisicionId: { } rqLineaId })
        {
            return;
        }

        try
        {
            var otrasOc = await _db.LineasOrdenCompra
                .Where(l => l.LineaRequisicionId == rqLineaId && l.Id != lineaOc.Id)
                .SumAsync(l => l.CantidadRecibida, cancellationToken);
            var rq = await _db.Requisiciones
                .Include(r => r.Lineas)
                .FirstOrDefaultAsync(r => r.Id == rqId, cancellationToken);
            rq?.AjustarRecepcionAlNeto(rqLineaId, otrasOc + lineaOc.CantidadRecibida);
        }
        catch (BusinessRuleException ex)
        {
            _logger.LogWarning(ex,
                "[OcDevolucionRegistradaListener] No se ajustó la RQ {RqId} (línea {LineaId}) tras la devolución en la OC {OcId}. La OC sí se ajustó.",
                rqId, rqLineaId, oc.Id);
        }
    }
}

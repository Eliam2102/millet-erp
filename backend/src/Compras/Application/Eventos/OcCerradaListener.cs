using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Events;
using Millet.Compras.Domain.Ports.OrdenCompra;

namespace Millet.Compras.Application.Eventos;

/// <summary>
/// Handler in-proc (F5-PR1) que reacciona a
/// <see cref="OcCerradaEvent"/> (emitido por el submódulo OC cuando
/// una OC se cierra). Si la cantidad entregada es menor que la
/// solicitada, publica <see cref="SaldoNoSurtidoEvent"/> — informativo,
/// no abre re-autorización (asunción A12).
///
/// <para>
/// Si <c>entregada == solicitada</c>, no hay saldo y el handler es
/// no-op (la línea ya cerró por la recepción correspondiente).
/// </para>
/// </summary>
public sealed class OcCerradaListener : INotificationHandler<OcCerradaEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<OcCerradaListener> _logger;

    public OcCerradaListener(IMediator mediator, ILogger<OcCerradaListener> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OcCerradaEvent notification, CancellationToken cancellationToken)
    {
        var saldo = notification.CantidadSolicitada - notification.CantidadEntregada;

        if (saldo <= 0)
        {
            _logger.LogDebug(
                "OC cerrada sin saldo. RequisicionId={RequisicionId} OcId={OcId} Solicitada={Solicitada} Entregada={Entregada}",
                notification.RequisicionId,
                notification.OrdenCompraId,
                notification.CantidadSolicitada,
                notification.CantidadEntregada);
            return;
        }

        _logger.LogWarning(
            "OC cerrada con saldo no surtido. RequisicionId={RequisicionId} OcId={OcId} Solicitada={Solicitada} Entregada={Entregada} Saldo={Saldo}",
            notification.RequisicionId,
            notification.OrdenCompraId,
            notification.CantidadSolicitada,
            notification.CantidadEntregada,
            saldo);

        await _mediator.Publish(
            new SaldoNoSurtidoEvent(
                RequisicionId: notification.RequisicionId,
                LineaRequisicionId: notification.LineaRequisicionId,
                OrdenCompraId: notification.OrdenCompraId,
                EmpresaId: notification.EmpresaId,
                CantidadSolicitada: notification.CantidadSolicitada,
                CantidadEntregada: notification.CantidadEntregada,
                Saldo: saldo,
                OcurridoEn: notification.OcurridoEn),
            cancellationToken);
    }
}

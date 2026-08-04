using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Application.RegistrarRecepcion;
using Millet.Compras.Domain.Ports.OrdenCompra;

namespace Millet.Compras.Application.Eventos;

/// <summary>
/// Handler in-proc (F5-PR1) que reacciona a
/// <see cref="OcRecepcionRegistradaEvent"/> (emitido por el submódulo
/// OC cuando registra una recepción de material) y lo traduce a un
/// <see cref="RegistrarRecepcionCommand"/> ejecutado vía MediatR.
///
/// <para>
/// Bridge in-proc entre OC y Compras: ambos viven en el mismo monolito,
/// pero el desacoplamiento por eventos prepara el camino para que OC
/// se mueva eventualmente a Service Bus / Outbox sin tocar Compras.
/// </para>
/// </summary>
public sealed class OcRecepcionRegistradaListener : INotificationHandler<OcRecepcionRegistradaEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<OcRecepcionRegistradaListener> _logger;

    public OcRecepcionRegistradaListener(
        IMediator mediator,
        ILogger<OcRecepcionRegistradaListener> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task Handle(OcRecepcionRegistradaEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Recepción OC recibida. RequisicionId={RequisicionId} LineaId={LineaId} OcId={OcId} Cantidad={Cantidad}",
            notification.RequisicionId,
            notification.LineaRequisicionId,
            notification.OrdenCompraId,
            notification.CantidadRecibida);

        await _mediator.Send(
            new RegistrarRecepcionCommand(
                RequisicionId: notification.RequisicionId,
                LineaRequisicionId: notification.LineaRequisicionId,
                CantidadRecibida: notification.CantidadRecibida,
                OcurridoEn: notification.OcurridoEn),
            cancellationToken);
    }
}

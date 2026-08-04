using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Events;

namespace Millet.Compras.Application.Eventos;

/// <summary>
/// Handler in-proc de <see cref="SaldoNoSurtidoEvent"/> (F5-PR1):
/// loggea estructurado el saldo no entregado para auditoría operativa.
/// Per A12, este evento es informativo — no abre re-autorización.
/// </summary>
public sealed class SaldoNoSurtidoLoggingHandler : INotificationHandler<SaldoNoSurtidoEvent>
{
    private readonly ILogger<SaldoNoSurtidoLoggingHandler> _logger;

    public SaldoNoSurtidoLoggingHandler(ILogger<SaldoNoSurtidoLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(SaldoNoSurtidoEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Saldo no surtido (informativo, A12). RequisicionId={RequisicionId} LineaId={LineaId} OcId={OcId} Solicitada={Solicitada} Entregada={Entregada} Saldo={Saldo}",
            notification.RequisicionId,
            notification.LineaRequisicionId,
            notification.OrdenCompraId,
            notification.CantidadSolicitada,
            notification.CantidadEntregada,
            notification.Saldo);
        return Task.CompletedTask;
    }
}

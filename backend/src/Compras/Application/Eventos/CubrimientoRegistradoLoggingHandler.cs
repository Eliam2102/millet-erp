using MediatR;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Events;

namespace Millet.Compras.Application.Eventos;

/// <summary>
/// Handler in-proc de <see cref="CubrimientoRegistradoEvent"/> (F4-PR4):
/// loggea estructurado el snapshot de cubrimiento por línea + estado
/// final tras la bifurcación stock-aware. Útil para diagnostics y
/// auditoría operativa.
/// </summary>
public sealed class CubrimientoRegistradoLoggingHandler
    : INotificationHandler<CubrimientoRegistradoEvent>
{
    private readonly ILogger<CubrimientoRegistradoLoggingHandler> _logger;

    public CubrimientoRegistradoLoggingHandler(ILogger<CubrimientoRegistradoLoggingHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(CubrimientoRegistradoEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Cubrimiento registrado. RequisicionId={RequisicionId} EmpresaId={EmpresaId} EstadoFinal={EstadoFinal} LineasCount={LineasCount} OcurridoEn={OcurridoEn}",
            notification.RequisicionId,
            notification.EmpresaId,
            notification.EstadoFinal,
            notification.Lineas.Count,
            notification.OcurridoEn);
        return Task.CompletedTask;
    }
}

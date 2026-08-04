using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// Stub del puerto de asientos contables (F10-PR1). Loggea el asiento con cuentas
/// <c>TBD-*</c> hasta que el módulo Contabilidad exista y consuma los eventos del
/// topic <c>facturacion-events</c>. PLATFORM-TODO(&lt;ContabilidadAsientos&gt;).
/// </summary>
public sealed class NoOpContabilidadAsientoPort : IContabilidadAsientoPort
{
    private readonly ILogger<NoOpContabilidadAsientoPort> _logger;

    public NoOpContabilidadAsientoPort(ILogger<NoOpContabilidadAsientoPort> logger) => _logger = logger;

    public Task RegistrarAsientoAsync(AsientoContableSolicitud solicitud, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[NoOpContabilidadAsientoPort] Asiento {Tipo} comprobante={Id} total={Total} {Moneda} → cuentas TBD-* " +
            "(PLATFORM-TODO<ContabilidadAsientos>).",
            solicitud.TipoComprobante, solicitud.ComprobanteId, solicitud.Total, solicitud.Moneda);
        return Task.CompletedTask;
    }
}

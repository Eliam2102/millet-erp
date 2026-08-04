using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.Almacen;

namespace Millet.CuentasPorPagar.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="IAlmacenRecepcionReadPort"/> (F0-PR1). Devuelve
/// <c>null</c>. La captura de factura sigue funcionando porque v1 no
/// bloquea por three-way match (§1.2 último punto del 01-diseno).
///
/// PLATFORM-TODO(&lt;AlmacenRecepcion&gt;): cuando el módulo Almacén esté
/// en runtime, reemplazar por adapter que proyecte
/// <c>OcRecepcionRegistradaEvent</c> a una tabla local de CxP.
/// </summary>
public sealed class NoOpAlmacenRecepcionReadPort : IAlmacenRecepcionReadPort
{
    private readonly ILogger<NoOpAlmacenRecepcionReadPort> _logger;

    public NoOpAlmacenRecepcionReadPort(ILogger<NoOpAlmacenRecepcionReadPort> logger)
    {
        _logger = logger;
    }

    public Task<RecepcionOcDto?> ObtenerRecepcionDeOcAsync(Guid ordenCompraId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOp] IAlmacenRecepcionReadPort.ObtenerRecepcionDeOcAsync ocId={OcId}", ordenCompraId);
        return Task.FromResult<RecepcionOcDto?>(null);
    }
}

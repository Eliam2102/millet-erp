using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

/// <summary>
/// Stub del catálogo de Activos Fijos (F9). Devuelve <c>null</c> hasta que exista
/// el módulo Activos Fijos: sin él, la autorización de venta de activos se rechaza
/// (no se puede validar el alta ni el valor en libros).
/// PLATFORM-TODO(&lt;ActivosFijos&gt;).
/// </summary>
public sealed class NoOpActivosFijosReadPort : IActivosFijosReadPort
{
    private readonly ILogger<NoOpActivosFijosReadPort> _logger;

    public NoOpActivosFijosReadPort(ILogger<NoOpActivosFijosReadPort> logger) => _logger = logger;

    public Task<ActivoFijoLectura?> ObtenerAsync(string activoRef, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpActivosFijosReadPort] Sin módulo Activos Fijos — activo {Ref} no resoluble. PLATFORM-TODO(<ActivosFijos>).", activoRef);
        return Task.FromResult<ActivoFijoLectura?>(null);
    }
}

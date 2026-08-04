using Microsoft.Extensions.Logging;
using Millet.Tesoreria.Domain.Ports;

namespace Millet.Tesoreria.Infrastructure.Stubs;

/// <summary>
/// PLATFORM-TODO(&lt;PeriodoContableCerrado&gt;): adapter real cuando exista
/// el módulo Contabilidad con el calendario fiscal central. Comportamiento
/// del stub: siempre "abierto" (true) — mismo criterio que el NoOp de
/// Facturación; mientras tanto los no-aplicados al cierre se reportan
/// explícitamente (levantamiento §3.4 paso 5).
/// </summary>
public sealed class NoOpPeriodoContablePort : IPeriodoContablePort
{
    private readonly ILogger<NoOpPeriodoContablePort> _logger;

    public NoOpPeriodoContablePort(ILogger<NoOpPeriodoContablePort> logger) => _logger = logger;

    public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpPeriodoContablePort] {Anio}/{Mes:00} → abierto (stub TES-PR2)", año, mes);
        return Task.FromResult(true);
    }
}

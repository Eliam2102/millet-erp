using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.Stubs;

// ============================================================================
// Stubs NoOp de los puertos cross-module que aún no tienen adapter real
// (F0-PR1). Cada uno lleva su propio PLATFORM-TODO buscable con
// `rg "PLATFORM-TODO" backend/src/Facturacion` y su fila en §13/§14 del
// diseño (ADR-0031). Los stubs devuelven el mínimo coherente para no
// bloquear el smoke del módulo; los handlers reales validan contra el
// adapter productivo en la fase del puerto correspondiente.
//
// Nota: NoOpClientesReadPort / NoOpProductosReadPort (<DatosMaestrosFiscal>)
// se retiraron — los adapters reales viven en
// Infrastructure/DatosMaestros/ (compartido.clientes + compartido.producto_aw,
// ADR-0048 D5/D6).
// ============================================================================

/// <summary>
/// PLATFORM-TODO(&lt;PeriodoContableCerrado&gt;): adapter real cuando exista el
/// módulo Contabilidad con el calendario fiscal central. Comportamiento del
/// stub: siempre "abierto" (true) — preserva la operación durante el desarrollo
/// y mitiga el candado de período (D13) con validación manual hasta el wireup.
/// </summary>
public sealed class NoOpPeriodoContablePort : IPeriodoContablePort
{
    private readonly ILogger<NoOpPeriodoContablePort> _logger;

    public NoOpPeriodoContablePort(ILogger<NoOpPeriodoContablePort> logger) => _logger = logger;

    public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken)
    {
        _logger.LogDebug("[NoOpPeriodoContablePort] {Anio}/{Mes:00} → abierto (stub F0-PR1)", año, mes);
        return Task.FromResult(true);
    }
}

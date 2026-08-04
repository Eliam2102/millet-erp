using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Liveness check del <see cref="AwLateReconciliationWorker"/>: <c>Healthy</c>
/// si el loop principal está corriendo. <c>Unhealthy</c> si el worker no
/// arrancó o terminó por excepción no atrapada.
///
/// <para>
/// <b>Liveness, NO readiness:</b> no verifica conectividad real al endpoint
/// <c>/completions</c>. Si el endpoint está down, los errores se
/// contabilizan en <c>aw.cotizacion.late_reconciliation_errors</c> y el
/// worker sigue intentando en el siguiente ciclo.
/// </para>
/// </summary>
public sealed class AwLateReconciliationWorkerHealthCheck : IHealthCheck
{
    private readonly AwLateReconciliationWorker _worker;

    public AwLateReconciliationWorkerHealthCheck(AwLateReconciliationWorker worker)
    {
        _worker = worker;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_worker.IsRunning
            ? HealthCheckResult.Healthy("AwLateReconciliationWorker está corriendo.")
            : HealthCheckResult.Unhealthy("AwLateReconciliationWorker NO está corriendo."));
    }
}

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Liveness check del <see cref="AwDocumentSyncWorker"/>: <c>Healthy</c>
/// si el loop principal está corriendo. <c>Unhealthy</c> si el worker no
/// arrancó o terminó por excepción no atrapada.
///
/// <para>
/// <b>Liveness, NO readiness:</b> no verifica conectividad real al endpoint
/// <c>/documents</c>. Si el endpoint está down, los errores se contabilizan
/// en <c>aw.documento.sync_errors</c> y el worker reintenta en el siguiente
/// ciclo.
/// </para>
/// </summary>
public sealed class AwDocumentSyncWorkerHealthCheck : IHealthCheck
{
    private readonly AwDocumentSyncWorker _worker;

    public AwDocumentSyncWorkerHealthCheck(AwDocumentSyncWorker worker)
    {
        _worker = worker;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_worker.IsRunning
            ? HealthCheckResult.Healthy("AwDocumentSyncWorker está corriendo.")
            : HealthCheckResult.Unhealthy("AwDocumentSyncWorker NO está corriendo."));
    }
}

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Millet.Integraciones.Aw.Application.Workers;

/// <summary>
/// Liveness check del <see cref="AwDropWorker"/>: <c>Healthy</c> si el
/// processor de Service Bus está corriendo (i.e. el Start tuvo éxito y
/// no hubo shutdown). Reporta <c>Unhealthy</c> si el worker no está
/// running por cualquier motivo (Start falló, Stop completó, exception
/// no atrapada en ExecuteAsync).
///
/// <para>
/// <b>Liveness, NO readiness:</b> este check NO verifica conectividad
/// real al Service Bus o al on-prem — eso es PLATFORM-TODO
/// &lt;HybridConnectionHealthcheck&gt;. Si el SB está down, el
/// <c>ProcessorErrorAsync</c> loguea el error pero el processor sigue
/// "running" (intentando reconectar internamente). Métrica
/// <c>aw.cotizacion.drop_failed</c> es la señal real de salud aplicativa.
/// </para>
///
/// <para>
/// <b>Patrón singleton + IHostedService:</b> el worker está registrado
/// dos veces (singleton + IHostedService) apuntando a la MISMA instancia
/// — sin esto, este healthcheck recibiría una instancia nueva y el flag
/// <c>IsRunning</c> nunca sería true. Ver
/// <see cref="DependencyInjection.AddIntegracionesAwModule"/>.
/// </para>
/// </summary>
public sealed class AwDropWorkerHealthCheck : IHealthCheck
{
    private readonly AwDropWorker _worker;

    public AwDropWorkerHealthCheck(AwDropWorker worker)
    {
        _worker = worker;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_worker.IsRunning
            ? HealthCheckResult.Healthy("AwDropWorker está corriendo.")
            : HealthCheckResult.Unhealthy("AwDropWorker NO está corriendo (Start falló o ya se detuvo)."));
    }
}

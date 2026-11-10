using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Millet.Api.Correo;

/// <summary>
/// Aviso de vencimiento del client secret con Mail.Send (86e3zr74h). El valor
/// vive en Key Vault y vence el 2027-01-27; <c>Correo:SecretoVenceEl</c> guarda
/// solo la fecha. Mismo patrón que el certificado de SER-DATA: degradado dentro
/// de la ventana de aviso y no sano ya vencido.
/// </summary>
public sealed record VencimientoSecretoCorreo(DateTimeOffset VenceEl, TimeSpan Aviso)
{
    public HealthCheckResult Evaluar(DateTimeOffset ahora)
    {
        if (VenceEl <= ahora)
            return HealthCheckResult.Unhealthy(
                $"El secreto de Graph para enviar correo venció el {VenceEl:yyyy-MM-dd}: el ERP no puede mandar correo. Renuévelo en Entra ID y Key Vault y actualice Correo:SecretoVenceEl.");
        if (VenceEl - ahora < Aviso)
            return HealthCheckResult.Degraded(
                $"El secreto de Graph para enviar correo vence el {VenceEl:yyyy-MM-dd} (en {(VenceEl - ahora).Days} días). Renuévelo en Entra ID y Key Vault y actualice Correo:SecretoVenceEl.");
        return HealthCheckResult.Healthy($"Secreto de Graph para correo vigente hasta {VenceEl:yyyy-MM-dd}.");
    }
}

public sealed class VencimientoSecretoCorreoHealthCheck(VencimientoSecretoCorreo vencimiento) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(vencimiento.Evaluar(DateTimeOffset.UtcNow));
}

/// <summary>Deja el aviso en el log (Application Insights) al arrancar y una vez al día mientras aplique.</summary>
public sealed class VencimientoSecretoCorreoWorker(
    VencimientoSecretoCorreo vencimiento, ILogger<VencimientoSecretoCorreoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            var r = vencimiento.Evaluar(DateTimeOffset.UtcNow);
            if (r.Status == HealthStatus.Unhealthy) logger.LogError("[Correo] {Aviso}", r.Description);
            else if (r.Status == HealthStatus.Degraded) logger.LogWarning("[Correo] {Aviso}", r.Description);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

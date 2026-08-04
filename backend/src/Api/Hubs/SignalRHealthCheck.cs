using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Millet.Api.Hubs;

/// <summary>
/// Health check del wiring del CollaborationHub (Sprint Buffer, ADR-0019).
/// Verifica que la instancia puede servir tráfico al hub:
///
/// <list type="bullet">
///   <item>El <see cref="ISoftLockManager"/> singleton es resoluble — si el
///         DI está roto, falla aquí en lugar de explotar en runtime.</item>
///   <item>En Production, <c>SignalR:ConnectionString</c> debe estar
///         configurada (vía Key Vault reference). Si está vacía, la
///         instancia corre con backplane in-process — ok para 1 nodo,
///         <em>roto</em> para multi-instancia (un usuario en el nodo A no
///         vería los broadcasts del nodo B). Marcamos unhealthy para que
///         App Service lo saque del pool y el operador investigue
///         (típicamente Key Vault reference rota).</item>
///   <item>Si la connection string está presente, debe parsear como un
///         endpoint+accessKey válido — atrapa rotaciones que dejaron una
///         cadena vacía o malformada.</item>
/// </list>
///
/// <para>
/// El check NO hace round-trip a Azure SignalR Service. La salud del
/// servicio gestionado se monitorea por las metric alerts del Sprint 3a
/// (<c>SystemErrors</c>, <c>ServerLoad</c>) — esa es la capa correcta.
/// <c>/health/ready</c> debe responder rápido (App Service lo poll cada
/// ~30s) y reflejar SOLO la salud del proceso local.
/// </para>
/// </summary>
public sealed class SignalRHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ISoftLockManager _manager;

    public SignalRHealthCheck(
        IConfiguration configuration,
        IHostEnvironment environment,
        ISoftLockManager manager)
    {
        _configuration = configuration;
        _environment = environment;
        _manager = manager;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = _configuration["SignalR:ConnectionString"];
        var hasConnectionString = !string.IsNullOrWhiteSpace(connectionString);

        if (_environment.IsProduction() && !hasConnectionString)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "SignalR:ConnectionString vacía en Production. " +
                "Verificar Key Vault reference @Microsoft.KeyVault(VaultName=...;SecretName=signalr-connection-string)."));
        }

        if (hasConnectionString && !LooksLikeSignalRConnectionString(connectionString!))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "SignalR:ConnectionString tiene formato inválido. " +
                "Esperado: Endpoint=https://...;AccessKey=...;Version=1.0;"));
        }

        // Toca el manager: si la singleton no está bien wireada, falla aquí.
        var activeLocks = _manager.Snapshot().Count;

        var data = new Dictionary<string, object>
        {
            ["mode"] = hasConnectionString ? "azure-signalr" : "in-process",
            ["activeLocks"] = activeLocks,
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            $"hub mode={data["mode"]} activeLocks={activeLocks}",
            data));
    }

    private static bool LooksLikeSignalRConnectionString(string value) =>
        value.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase)
        && value.Contains("AccessKey=", StringComparison.OrdinalIgnoreCase);
}

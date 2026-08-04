using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Workers;

namespace Millet.Integraciones.Aw.IntegrationTests.Wiring;

/// <summary>
/// Smoke tests del wiring DI del módulo PR C. Verifican que con el host
/// real levantado (WebApplicationFactory&lt;Program&gt;):
///
/// <list type="bullet">
///   <item>Los adapters reales están registrados (HttpAwDropAdapter,
///         HybridConnectionAwSqlReader vía ISqlConnectionFactory).</item>
///   <item>Los workers están registrados como singleton + IHostedService
///         apuntando a la MISMA instancia (recipe del prompt v3 — sin
///         esto el healthcheck recibe una instancia distinta y reporta
///         Unhealthy aunque el worker corra).</item>
///   <item>Los healthchecks están registrados con tag "liveness".</item>
///   <item>El notifier productivo (IntegracionesAwHubNotifier) está
///         registrado por Program.cs.</item>
/// </list>
///
/// <para>
/// Tests de flujo end-to-end (drop completo, polling completo) requieren
/// stubs de SQL Server / Service Bus en el WebApplicationFactory; se
/// difieren a un PR posterior con infrastructura de stubbing apropiada.
/// </para>
/// </summary>
public class PrcWiringTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PrcWiringTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void HttpAwDropAdapter_RegistradoComoIAwDropAdapter()
    {
        using var scope = _factory.Services.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IAwDropAdapter>();
        adapter.Should().NotBeNull();
    }

    [Fact]
    public void SqlConnectionFactory_RegistradoComoSingleton()
    {
        var f1 = _factory.Services.GetRequiredService<
            Millet.Integraciones.Aw.Infrastructure.Adapters.ISqlConnectionFactory>();
        var f2 = _factory.Services.GetRequiredService<
            Millet.Integraciones.Aw.Infrastructure.Adapters.ISqlConnectionFactory>();
        f1.Should().BeSameAs(f2); // singleton
    }

    [Fact]
    public void HybridConnectionAwSqlReader_RegistradoComoIAwSqlReader()
    {
        using var scope = _factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAwSqlReader>();
        reader.Should().NotBeNull();
    }

    [Fact]
    public void Notifier_Productivo_RegistradoEnApi()
    {
        var notifier = _factory.Services.GetRequiredService<IIntegracionesAwNotifier>();
        notifier.Should().NotBeNull();
        notifier.GetType().Name.Should().Be("IntegracionesAwHubNotifier");
    }

    [Fact]
    public void AwDropWorker_Singleton_MismaInstanciaQueIHostedService()
    {
        // Recipe crítica del prompt v3: AddSingleton<T> + AddHostedService(sp => sp.Get<T>())
        // garantiza que el healthcheck (que inyecta T directamente) recibe
        // la misma instancia que el host está ejecutando. Sin esto, el flag
        // _running queda invisible al healthcheck.
        var workerAsSingleton = _factory.Services.GetRequiredService<AwDropWorker>();
        var hostedServices = _factory.Services
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<AwDropWorker>()
            .ToList();

        hostedServices.Should().ContainSingle();
        hostedServices.Single().Should().BeSameAs(workerAsSingleton);
    }

    [Fact]
    public async Task Healthcheck_AwDropWorker_RegistradoConTagLiveness()
    {
        // PR #201 retiró AwCorrelationWorker → ya no hay aw-correlation-worker
        // healthcheck. Solo aw-drop-worker permanece.
        var hcService = _factory.Services.GetRequiredService<HealthCheckService>();
        var report = await hcService.CheckHealthAsync(
            r => r.Tags.Contains("liveness"));

        report.Entries.Should().ContainKey("aw-drop-worker");
    }

    [Fact]
    public void Meter_IntegracionesAw_Existe()
    {
        // Smoke: el Meter es static — verificamos que las métricas están
        // declaradas y son inicializables.
        IntegracionesAwMeter.DropSuccess.Should().NotBeNull();
        IntegracionesAwMeter.Correlated.Should().NotBeNull();
        IntegracionesAwMeter.CorrelacionRechazada.Should().NotBeNull();
        IntegracionesAwMeter.DropWaitDurationMs.Should().NotBeNull();
        IntegracionesAwMeter.Name.Should().Be("Millet.Integraciones.Aw");
    }
}

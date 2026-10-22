using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Infrastructure;
using Xunit;

namespace Millet.Integraciones.Aw.UnitTests.Workers;

/// <summary>
/// Interruptor <c>Habilitado</c> de AwDocumentSyncWorker y AwLateReconciliationWorker:
/// apagado no registra ni el worker ni su health check.
/// </summary>
public sealed class AwWorkersRegistrationTests
{
    private static ServiceCollection Registrar(Dictionary<string, string?> config)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var services = new ServiceCollection();
        services.AddIntegracionesAwModule(cfg);
        return services;
    }

    private static bool TieneWorker<T>(IServiceCollection s) =>
        s.Any(d => d.ServiceType == typeof(T))
        && s.Any(d => d.ServiceType == typeof(IHostedService));

    private static bool TieneHealthCheck(IServiceCollection s, string nombre)
    {
        using var sp = s.BuildServiceProvider();
        return sp.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Any(r => r.Name == nombre);
    }

    [Fact]
    public void DocumentSync_Apagado_NoRegistraWorkerNiHealthCheck()
    {
        var s = Registrar(new() { ["IntegracionesAw:DocumentSync:Habilitado"] = "false" });

        s.Any(d => d.ServiceType == typeof(AwDocumentSyncWorker)).Should().BeFalse();
        TieneHealthCheck(s, "aw-document-sync-worker").Should().BeFalse();
        // Los otros siguen y el kick (usado por Drop y LateReconciliation) no se toca.
        s.Any(d => d.ServiceType == typeof(DocumentSyncKick)).Should().BeTrue();
        s.Any(d => d.ServiceType == typeof(AwLateReconciliationWorker)).Should().BeTrue();
    }

    [Fact]
    public void LateReconciliation_Apagado_NoRegistraWorkerNiHealthCheck()
    {
        var s = Registrar(new() { ["IntegracionesAw:LateReconciliation:Habilitado"] = "false" });

        s.Any(d => d.ServiceType == typeof(AwLateReconciliationWorker)).Should().BeFalse();
        TieneHealthCheck(s, "aw-late-reconciliation-worker").Should().BeFalse();
        s.Any(d => d.ServiceType == typeof(AwDocumentSyncWorker)).Should().BeTrue();
    }

    [Fact]
    public void SinLlave_RegistraAmbosWorkersYSusHealthChecks()
    {
        var s = Registrar(new());

        TieneWorker<AwDocumentSyncWorker>(s).Should().BeTrue();
        TieneWorker<AwLateReconciliationWorker>(s).Should().BeTrue();
        TieneHealthCheck(s, "aw-document-sync-worker").Should().BeTrue();
        TieneHealthCheck(s, "aw-late-reconciliation-worker").Should().BeTrue();
    }
}

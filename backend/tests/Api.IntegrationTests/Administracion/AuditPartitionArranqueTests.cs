using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Fix de arranque: la partición del mes de <c>core.audit_log</c> la crea <see cref="AuditPartitionRolloverJob"/> al
/// arrancar. Los hosted services arrancan en orden de registro; si un seed, el outbox o un worker escribe antes, en una
/// base nueva de un mes sin partición falla con 23514 «no partition of relation "audit_log" found for row» (pasó el
/// 1-nov-2026 en todos los gates).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class AuditPartitionArranqueTests(WebApplicationFactory<Program> factory)
{
    [Fact]
    public void El_job_de_particiones_arranca_antes_que_cualquier_otro_hosted_service_del_ERP()
    {
        var milletHostedServices = factory.Services.GetServices<IHostedService>()
            .Select(h => h.GetType())
            .Where(t => t.Namespace?.StartsWith("Millet.", StringComparison.Ordinal) == true)
            .ToList();

        milletHostedServices.Should().NotBeEmpty();
        milletHostedServices[0].Should().Be<AuditPartitionRolloverJob>(
            "ningún seed, outbox ni worker debe escribir en la base antes de que exista la partición del mes");
    }
}

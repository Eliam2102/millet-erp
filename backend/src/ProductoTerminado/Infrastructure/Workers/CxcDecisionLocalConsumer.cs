using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.ProductoTerminado.Application.Liberacion;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ProductoTerminado.Infrastructure.Workers;

/// <summary>
/// Escucha <c>cuentas_por_cobrar.decision-liberacion.emitida.v1</c> y libera el pedido retenido (motivo DecisionCxc).
/// Con Service Bus lo consume <c>producto-terminado-cxc-sub</c> en <c>cuentas-por-cobrar-events</c> (M-046).
/// </summary>
public sealed class CxcDecisionLocalConsumer(IServiceScopeFactory scopes, ILogger<CxcDecisionLocalConsumer> logger) : ILocalIntegrationEventConsumer
{
    public const string EventType = "cuentas_por_cobrar.decision-liberacion.emitida.v1";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string TopicName => "cuentas-por-cobrar-events";

    public async Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
    {
        if (eventType != EventType) return;
        var p = JsonSerializer.Deserialize<DecisionPayload>(body, Json) ?? throw new JsonException("Payload null.");
        using var scope = scopes.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var liberado = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new AplicarDecisionCxcCommand(p.EmpresaId, p.PedidoRef, p.Resultado, p.DecididoPor), cancellationToken);
        logger.LogInformation("Decisión de CxC {Resultado} del pedido {PedidoRef}: {Efecto}", p.Resultado, p.PedidoRef, liberado ? "liberado" : "sin cambio");
    }

    private sealed record DecisionPayload(Guid EmpresaId, string PedidoRef, string Resultado, Guid? DecididoPor = null);
}

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Application.Liberacion;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorCobrar.Infrastructure.Workers;

/// <summary>
/// PT5.2: al recibir <c>producto_terminado.pedido.estado-cambiado.v1</c> con <c>Retenido</c> pide la decisión de liberación. PT5.7: con <c>producto_terminado.contra-entrega.no-liquidada.v1</c> levanta la alerta de cartera.
/// PLATFORM-TODO(&lt;TopicProductoTerminado&gt;): solo entrega local (C1.8); con Service Bus falta el topic <c>producto-terminado-events</c> y su suscripción (§9 del diseño).
/// </summary>
public sealed class ProductoTerminadoLocalConsumer(IServiceScopeFactory scopes, ILogger<ProductoTerminadoLocalConsumer> logger) : ILocalIntegrationEventConsumer
{
    public const string EventType = "producto_terminado.pedido.estado-cambiado.v1";
    public const string ContraEntregaEventType = "producto_terminado.contra-entrega.no-liquidada.v1";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string TopicName => "producto-terminado-events";

    public async Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
    {
        if (eventType == ContraEntregaEventType)
        {
            var n = JsonSerializer.Deserialize<ContraEntregaPayload>(body, Json) ?? throw new JsonException("Payload null.");
            using var alcance = scopes.CreateScope();
            using var omitir = alcance.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var creada = await alcance.ServiceProvider.GetRequiredService<IMediator>().Send(
                new RegistrarAlertaContraEntregaCommand(n.EmpresaId, n.ClienteId, n.Moneda, n.Total, n.NumeroPedido, n.HojaSalidaFolio, n.SalidaAt), cancellationToken);
            logger.LogInformation("Contra entrega sin liquidar {Pedido}: {Efecto}", n.NumeroPedido, creada ? "alerta de cartera creada" : "sin alerta nueva");
            return;
        }
        if (eventType != EventType) return;
        var p = JsonSerializer.Deserialize<EstadoPayload>(body, Json) ?? throw new JsonException("Payload null.");
        if (p.Estado != "Retenido") return;
        using var scope = scopes.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var decidio = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(
            new SolicitarDecisionAlRetenerCommand(p.EmpresaId, p.NumeroPedido, p.ClienteId ?? Guid.Empty, p.Moneda ?? "", p.Total ?? 0m), cancellationToken);
        logger.LogInformation("Pedido retenido {Pedido}: {Efecto}", p.NumeroPedido, decidio ? "decisión de CxC emitida" : "sin decisión nueva");
    }

    private sealed record ContraEntregaPayload(Guid EmpresaId, Guid ClienteId, string Moneda, decimal Total, string NumeroPedido, string HojaSalidaFolio, DateTimeOffset SalidaAt);

    private sealed record EstadoPayload(Guid EmpresaId, string NumeroPedido, string Estado, Guid? ClienteId, string? Moneda, decimal? Total);
}

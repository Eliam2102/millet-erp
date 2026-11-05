using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.ProductoTerminado.Application.Entregas;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.ProductoTerminado.Infrastructure.Workers;

/// <summary>
/// PT5.6. Escucha <c>facturacion.factura-venta.timbrada.v1</c> (si el pedido ya está entregado pasa a 115) y <c>facturacion.cobro-mostrador.registrado.v1</c> (concilia el voucher que el chofer cobró en la entrega).
/// PLATFORM-TODO(&lt;TopicProductoTerminado&gt;): solo entrega local (C1.8); con Service Bus falta la suscripción de PT a <c>facturacion-events</c>, igual que la de CxC (§9 del diseño).
/// </summary>
public sealed class FacturacionLocalConsumer(IServiceScopeFactory scopes, ILogger<FacturacionLocalConsumer> logger) : ILocalIntegrationEventConsumer
{
    public const string FacturaTimbrada = "facturacion.factura-venta.timbrada.v1";
    public const string CobroRegistrado = "facturacion.cobro-mostrador.registrado.v1";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string TopicName => "facturacion-events";

    public async Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
    {
        if (eventType is not (FacturaTimbrada or CobroRegistrado)) return;
        using var scope = scopes.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        if (eventType == FacturaTimbrada)
        {
            var f = JsonSerializer.Deserialize<FacturaPayload>(body, Json) ?? throw new JsonException("Payload null.");
            if (f.PedidoFacturableId is not { } pedido) return;
            logger.LogInformation("Factura del pedido {Pedido}: {Efecto}", pedido, await mediator.Send(new AplicarFacturaPedidoCommand(f.EmpresaId, pedido), cancellationToken) ? "115" : "sin cambio");
        }
        else
        {
            var c = JsonSerializer.Deserialize<CobroPayload>(body, Json) ?? throw new JsonException("Payload null.");
            logger.LogInformation("Cobro {Cobro} de caja: {Efecto}", c.CobroMostradorId, await mediator.Send(new ConciliarCobroEntregaCommand(c.EmpresaId, c.CobroMostradorId, c.ComprobanteId, c.Total), cancellationToken) ? "voucher conciliado" : "sin entrega que conciliar");
        }
    }

    private sealed record FacturaPayload(Guid EmpresaId, Guid? PedidoFacturableId);
    private sealed record CobroPayload(Guid EmpresaId, Guid CobroMostradorId, Guid ComprobanteId, decimal Total);
}

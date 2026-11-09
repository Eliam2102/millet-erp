using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.ProductoTerminado.Application.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.Infrastructure.Retorno;

/// <summary>
/// 86e3jvv6z (respuesta de TI del 5-nov): escribe en <c>aw_retorno</c> los estatus que PT avisa. El 115
/// (<c>pedido.facturado-entregado.v1</c>) sigue yendo; del 70 (<c>pedido.estado-cambiado.v1</c> con <c>Liberado</c>) solo viaja la
/// liberación MANUAL de Crédito y Cobranza (motivo DecisionCxc, decidida por una persona): el 70 por saldo, pago o reparto lo
/// decide el servicio de A+W. Cada aviso va con el saldo vigente como cifra de control (tipo 2), nunca como fuente.
/// Con Service Bus lo consume <c>integraciones-aw-pt-sub</c> (M-046).
/// </summary>
public sealed class ProductoTerminadoRetornoLocalConsumer(IServiceScopeFactory scopes, ILogger<ProductoTerminadoRetornoLocalConsumer> logger)
    : ILocalIntegrationEventConsumer
{
    public const string EventTypeEstadoCambiado = "producto_terminado.pedido.estado-cambiado.v1";
    public const string EventTypeFacturadoEntregado = "producto_terminado.pedido.facturado-entregado.v1";
    /// <summary>Quien firma las decisiones automáticas de CxC (<c>SolicitarDecisionAlRetenerHandler.UsuarioSistemaId</c>): no es una liberación manual.</summary>
    public const string UsuarioSistemaCxc = "00000000-0000-0000-0000-00000000c0c0";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string TopicName => "producto-terminado-events";

    public async Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
    {
        if (eventType is not (EventTypeEstadoCambiado or EventTypeFacturadoEntregado)) return;
        var p = JsonSerializer.Deserialize<AvisoPayload>(body, Json) ?? throw new JsonException("Payload null.");
        var manual = eventType == EventTypeEstadoCambiado && p.Estado == "Liberado" && p.Motivo == "DecisionCxc"
            && !string.IsNullOrWhiteSpace(p.LiberadoPor) && p.LiberadoPor != UsuarioSistemaCxc;
        if (eventType == EventTypeEstadoCambiado && !manual) return; // Retenido, o 70 que decide el servicio de A+W

        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var servicio = sp.GetRequiredService<AwRetornoDocumentoService>();
        var secuencia = manual
            ? await servicio.RegistrarLiberacionManualAsync(p.NumeroPedido, p.LiberadoPor!, cancellationToken)
            : await servicio.RegistrarFacturadoEntregadoAsync(p.NumeroPedido, cancellationToken);
        logger.LogInformation("aw_retorno {Evento} del pedido {Pedido}: {Efecto}", manual ? "70 manual" : "115", p.NumeroPedido,
            secuencia is null ? "sin fila nueva" : $"secuencia {secuencia}");

        if (await sp.GetRequiredService<ISaldoPedidoReadPort>().ObtenerAsync(p.Empresa, p.NumeroPedido, cancellationToken) is { } saldo)
            await servicio.RegistrarSaldoControlAsync(p.NumeroPedido, saldo, cancellationToken);
    }

    private sealed record AvisoPayload(Guid Empresa, string NumeroPedido, string? Estado = null, string? Motivo = null, string? LiberadoPor = null);
}

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
/// decide el servicio de A+W. Respuesta de TI del 9-nov: el albarán (71) lo sigue emitiendo A+W, y el ERP manda el 72 al emitir
/// la hoja de salida (<c>pedido.en-ruta.v1</c>, solo la primera hoja del pedido). Cada aviso va con el saldo vigente como cifra de control (tipo 2), nunca como fuente.
/// Con Service Bus lo consume <c>integraciones-aw-pt-sub</c> (M-046).
/// </summary>
public sealed class ProductoTerminadoRetornoLocalConsumer(IServiceScopeFactory scopes, ILogger<ProductoTerminadoRetornoLocalConsumer> logger)
    : ILocalIntegrationEventConsumer
{
    public const string EventTypeEstadoCambiado = "producto_terminado.pedido.estado-cambiado.v1";
    public const string EventTypeEnRuta = "producto_terminado.pedido.en-ruta.v1";
    public const string EventTypeFacturadoEntregado = "producto_terminado.pedido.facturado-entregado.v1";
    /// <summary>Quien firma las decisiones automáticas de CxC (<c>SolicitarDecisionAlRetenerHandler.UsuarioSistemaId</c>): no es una liberación manual.</summary>
    public const string UsuarioSistemaCxc = "00000000-0000-0000-0000-00000000c0c0";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string TopicName => "producto-terminado-events";

    public static bool EsAviso(string eventType) => eventType is EventTypeEstadoCambiado or EventTypeFacturadoEntregado or EventTypeEnRuta;

    /// <summary>M-160: si A+W no responde por conexión, el aviso queda pendiente de reintento y el mensaje se completa.</summary>
    public async Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
    {
        if (!EsAviso(eventType)) return;
        if (!await AwRetornoAplicador.AplicarOPendienteAsync(scopes, nameof(ProductoTerminadoRetornoLocalConsumer), eventType, body, cancellationToken))
            logger.LogWarning("A+W no responde; el aviso {EventType} queda pendiente de reintento.", eventType);
    }

    /// <summary>Escribe el estatus (y su saldo de control) en aw_retorno con los servicios del scope dado.</summary>
    internal static async Task AplicarAsync(IServiceProvider sp, string eventType, string body, CancellationToken cancellationToken)
    {
        var p = JsonSerializer.Deserialize<AvisoPayload>(body, Json) ?? throw new JsonException("Payload null.");
        var manual = eventType == EventTypeEstadoCambiado && p.Estado == "Liberado" && p.Motivo == "DecisionCxc"
            && !string.IsNullOrWhiteSpace(p.LiberadoPor) && p.LiberadoPor != UsuarioSistemaCxc;
        if (eventType == EventTypeEstadoCambiado && !manual) return; // Retenido, o 70 que decide el servicio de A+W

        var servicio = sp.GetRequiredService<AwRetornoDocumentoService>();
        var enRuta = eventType == EventTypeEnRuta;
        var secuencia = manual
            ? await servicio.RegistrarLiberacionManualAsync(p.NumeroPedido, p.LiberadoPor!, cancellationToken)
            : enRuta
                ? await servicio.RegistrarHojaSalidaEmitidaAsync(p.NumeroPedido, cancellationToken)
                : await servicio.RegistrarFacturadoEntregadoAsync(p.NumeroPedido, cancellationToken);
        sp.GetService<ILogger<ProductoTerminadoRetornoLocalConsumer>>()?.LogInformation(
            "aw_retorno {Evento} del pedido {Pedido}: {Efecto}", manual ? "70 manual" : enRuta ? "72" : "115", p.NumeroPedido,
            secuencia is null ? "sin fila nueva" : $"secuencia {secuencia}");

        if (await sp.GetRequiredService<ISaldoPedidoReadPort>().ObtenerAsync(p.Empresa, p.NumeroPedido, cancellationToken) is { } saldo)
            await servicio.RegistrarSaldoControlAsync(p.NumeroPedido, saldo, cancellationToken);
    }

    private sealed record AvisoPayload(Guid Empresa, string NumeroPedido, string? Estado = null, string? Motivo = null, string? LiberadoPor = null);
}

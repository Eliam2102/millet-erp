using System.Text.Json;
using Azure.Messaging.ServiceBus;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.Retorno;

/// <summary>
/// Resultado del reproceso: aplicados en A+W, guardados como pendientes (A+W sigue sin responder), payload inválido
/// (se quedan en dead-letter) y otras fallas (se quedan en dead-letter para revisión).
/// </summary>
public sealed record ReprocesoDeadLetterRetornoAw(int Leidos, int Aplicados, int Pendientes, int Invalidos, int Fallidos);

/// <summary>
/// M-160: reprocesa la dead-letter de <c>integraciones-aw-subscription</c> (topic <c>facturacion-events</c>) con el mismo
/// servicio que el listener. Lo aplicado o guardado como pendiente se completa (sale de la dead-letter); la idempotencia de
/// <c>aw_retorno</c> evita duplicar. El payload corrupto y las demás fallas se quedan en la dead-letter.
/// </summary>
public sealed record ReprocesarDeadLetterRetornoAwCommand : IRequest<ReprocesoDeadLetterRetornoAw>;

public sealed class ReprocesarDeadLetterRetornoAwHandler(
    IServiceProvider sp, IServiceScopeFactory scopes, ILogger<ReprocesarDeadLetterRetornoAwHandler> logger)
    : IRequestHandler<ReprocesarDeadLetterRetornoAwCommand, ReprocesoDeadLetterRetornoAw>
{
    /// <summary>Tope por llamada: lo que quede se toma en la siguiente.</summary>
    internal const int MaxPorLlamada = 500;
    private const int Lote = 50;

    public async Task<ReprocesoDeadLetterRetornoAw> Handle(ReprocesarDeadLetterRetornoAwCommand request, CancellationToken cancellationToken)
    {
        var client = sp.GetService<ServiceBusClient>()
            ?? throw new BusinessRuleException("SERVICE_BUS_NO_CONFIGURADO",
                "Este ambiente no tiene Service Bus: no hay dead-letter que reprocesar.");
        await using var receiver = client.CreateReceiver(FacturacionRetornoListenerWorker.TopicName,
            FacturacionRetornoListenerWorker.SubscriptionName,
            new ServiceBusReceiverOptions { SubQueue = SubQueue.DeadLetter, ReceiveMode = ServiceBusReceiveMode.PeekLock });
        return await ReprocesarAsync(receiver, cancellationToken);
    }

    internal async Task<ReprocesoDeadLetterRetornoAw> ReprocesarAsync(ServiceBusReceiver receiver, CancellationToken ct)
    {
        int leidos = 0, aplicados = 0, pendientes = 0, invalidos = 0, fallidos = 0;
        // Los que se quedan en dead-letter vuelven a verse en la misma corrida; se dejan bloqueados (no se abandonan) hasta
        // terminar para no leerlos dos veces: el candado vence solo y quedan para la siguiente llamada.
        while (leidos < MaxPorLlamada)
        {
            var lote = await receiver.ReceiveMessagesAsync(Math.Min(Lote, MaxPorLlamada - leidos), TimeSpan.FromSeconds(5), ct);
            if (lote.Count == 0) break;
            foreach (var m in lote)
            {
                leidos++;
                var eventType = m.Subject ?? string.Empty;
                try
                {
                    if (!AwRetornoAplicador.EsAviso(eventType)) aplicados++; // nada que escribir: se descarta como el listener
                    else if (await AwRetornoAplicador.AplicarOPendienteAsync(scopes, nameof(ReprocesarDeadLetterRetornoAwHandler),
                                 eventType, m.Body.ToString(), ct)) aplicados++;
                    else pendientes++;
                    await receiver.CompleteMessageAsync(m, ct);
                }
                catch (JsonException ex)
                {
                    invalidos++;
                    logger.LogWarning(ex, "Dead-letter aw_retorno {MessageId} ({EventType}): payload inválido, se queda.", m.MessageId, eventType);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    fallidos++;
                    logger.LogError(ex, "Dead-letter aw_retorno {MessageId} ({EventType}): no se pudo reprocesar, se queda.", m.MessageId, eventType);
                }
            }
        }
        logger.LogInformation("Reproceso dead-letter aw_retorno: {Leidos} leídos, {Aplicados} aplicados, {Pendientes} pendientes, {Invalidos} inválidos, {Fallidos} con falla.",
            leidos, aplicados, pendientes, invalidos, fallidos);
        return new ReprocesoDeadLetterRetornoAw(leidos, aplicados, pendientes, invalidos, fallidos);
    }
}

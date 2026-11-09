using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.SharedKernel.UnitTests.Outbox;

/// <summary>M-046 · criterio 4: payload corrupto a dead-letter «InvalidPayload»; error técnico abandona; éxito completa.</summary>
public sealed class ServiceBusConsumerWorkerTests
{
    [Fact]
    public async Task Exito_completa_y_pasa_tipo_e_id_al_consumidor()
    {
        var consumidor = new Consumidor();
        var receptor = new Receptor();
        var id = Guid.NewGuid();

        await Procesar(consumidor, receptor, "producto_terminado.pedido.estado-cambiado.v1", "{}", id);

        receptor.Acciones.Should().Equal("complete");
        consumidor.Recibido.Should().Be(("producto_terminado.pedido.estado-cambiado.v1", id));
    }

    [Fact]
    public async Task Payload_corrupto_va_a_dead_letter_InvalidPayload()
    {
        var receptor = new Receptor();

        await Procesar(new Consumidor(() => throw new JsonException("malo")), receptor, "x.v1", "{", Guid.NewGuid());

        receptor.Acciones.Should().Equal("deadletter:InvalidPayload");
    }

    [Fact]
    public async Task Error_tecnico_abandona_para_reintento()
    {
        var receptor = new Receptor();

        await Procesar(new Consumidor(() => throw new InvalidOperationException("db caída")), receptor, "x.v1", "{}", Guid.NewGuid());

        receptor.Acciones.Should().Equal("abandon");
    }

    private static Task Procesar(Consumidor consumidor, Receptor receptor, string tipo, string cuerpo, Guid id)
    {
        var mensaje = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: BinaryData.FromString(cuerpo), messageId: id.ToString(), subject: tipo);
        var worker = new ServiceBusConsumerWorker(null!, consumidor, "sub", NullLogger.Instance);
        return worker.ProcesarAsync(new ProcessMessageEventArgs(mensaje, receptor, CancellationToken.None), CancellationToken.None);
    }

    private sealed class Consumidor(Action? alRecibir = null) : ILocalIntegrationEventConsumer
    {
        public (string Tipo, Guid Id)? Recibido { get; private set; }
        public string TopicName => "producto-terminado-events";

        public Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
        {
            alRecibir?.Invoke();
            Recibido = (eventType, eventId);
            return Task.CompletedTask;
        }
    }

    private sealed class Receptor : ServiceBusReceiver
    {
        public List<string> Acciones { get; } = [];

        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        { Acciones.Add("complete"); return Task.CompletedTask; }

        public override Task AbandonMessageAsync(ServiceBusReceivedMessage message, IDictionary<string, object>? propertiesToModify = null, CancellationToken cancellationToken = default)
        { Acciones.Add("abandon"); return Task.CompletedTask; }

        public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message, string deadLetterReason, string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default)
        { Acciones.Add($"deadletter:{deadLetterReason}"); return Task.CompletedTask; }
    }
}

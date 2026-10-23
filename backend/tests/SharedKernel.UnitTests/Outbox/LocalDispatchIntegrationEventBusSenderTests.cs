using Microsoft.Extensions.Logging.Abstractions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.SharedKernel.UnitTests.Outbox;

/// <summary>
/// C1.8 — entrega local de eventos de integración sin Service Bus.
/// </summary>
public sealed class LocalDispatchIntegrationEventBusSenderTests
{
    private static IntegrationEventOutboxEntry Entry(string eventType = "cuentas_por_pagar.factura.registrada.v1") =>
        new(Guid.CreateVersion7(), eventType, """{"facturaProveedorId":"x"}""", DateTimeOffset.UtcNow, Guid.NewGuid());

    private static LocalDispatchIntegrationEventBusSender Sender(params ILocalIntegrationEventConsumer[] consumers) =>
        new(consumers, NullLogger<LocalDispatchIntegrationEventBusSender>.Instance);

    [Fact]
    public async Task Entrega_solo_a_los_consumidores_del_topic_en_orden_con_id_tipo_y_payload_de_la_fila()
    {
        var bitacora = new List<string>();
        var a = new ConsumidorFalso("cuentas-por-pagar-events", "A", bitacora);
        var otro = new ConsumidorFalso("almacen-events", "OTRO", bitacora);
        var b = new ConsumidorFalso("cuentas-por-pagar-events", "B", bitacora);
        var entry = Entry();

        await Sender(a, otro, b).SendAsync(entry, "cuentas-por-pagar-events", CancellationToken.None);

        bitacora.Should().Equal("A", "B");
        foreach (var c in new[] { a, b })
        {
            c.Recibidos.Should().ContainSingle().Which.Should().Be((entry.EventType, entry.Id, entry.Payload));
        }
        otro.Recibidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Propaga_la_excepcion_del_consumidor_para_que_el_outbox_reintente()
    {
        var bitacora = new List<string>();
        var falla = new ConsumidorFalso("compras-events", "FALLA", bitacora, lanza: true);
        var siguiente = new ConsumidorFalso("compras-events", "SIGUIENTE", bitacora);

        var act = () => Sender(falla, siguiente).SendAsync(Entry(), "compras-events", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*FALLA*");
        siguiente.Recibidos.Should().BeEmpty();
    }

    [Fact]
    public async Task Topic_sin_consumidores_se_da_por_entregado()
    {
        var act = () => Sender(new ConsumidorFalso("almacen-events", "A", []))
            .SendAsync(Entry(), "admin-events", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Sin_consumidores_registrados_no_falla()
    {
        var act = () => Sender().SendAsync(Entry(), "compras-events", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private sealed class ConsumidorFalso(string topic, string nombre, List<string> bitacora, bool lanza = false)
        : ILocalIntegrationEventConsumer
    {
        public List<(string EventType, Guid EventId, string Body)> Recibidos { get; } = [];
        public string TopicName => topic;

        public Task ConsumirLocalAsync(string eventType, Guid eventId, string body, CancellationToken cancellationToken)
        {
            if (lanza) throw new InvalidOperationException($"{nombre} falló");
            bitacora.Add(nombre);
            Recibidos.Add((eventType, eventId, body));
            return Task.CompletedTask;
        }
    }
}

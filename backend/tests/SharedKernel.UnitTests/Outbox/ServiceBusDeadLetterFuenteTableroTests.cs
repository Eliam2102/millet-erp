using Microsoft.Extensions.Logging.Abstractions;
using Millet.SharedKernel.Application.Integration.Tablero;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.SharedKernel.UnitTests.Outbox;

/// <summary>I13.6 · QA O2: sin Service Bus la cola de errores no desaparece del tablero; se lee como «sin conexión».</summary>
public sealed class ServiceBusDeadLetterFuenteTableroTests
{
    [Fact]
    public async Task Sin_conexion_avisa_que_no_puede_leer_la_cola_de_errores()
    {
        var fuente = new ServiceBusDeadLetterFuenteTablero(null, SuscripcionServiceBus.DelErp,
            NullLogger<ServiceBusDeadLetterFuenteTablero>.Instance);

        var act = () => fuente.LeerAsync(DateTimeOffset.UnixEpoch, 10, default);

        (await act.Should().ThrowAsync<FuenteTableroSinConexionException>()).Which.Message.Should().Contain("Sin conexión a Service Bus");
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Devoluciones;

public sealed class ProyectarReembolsoNotaCreditoHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static NotaCreditoReembolsoSolicitadoPayload Payload(Guid ncId, decimal importe = 800m) => new(
        Guid.NewGuid(), Ahora, ncId, "NC-100", "UUID-NC", Guid.NewGuid(), importe, 1_000m, "MXN",
        Guid.NewGuid(), "Bonificación", Guid.NewGuid());

    [Fact]
    public async Task Dos_eventos_de_la_misma_NC_crean_una_sola_devolucion()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarReembolsoNotaCreditoHandler(db, new FakeClock(Ahora));
        var ncId = Guid.NewGuid();
        var payload = Payload(ncId);

        await handler.Handle(new ProyectarReembolsoNotaCreditoCommand(Guid.NewGuid(), payload), CancellationToken.None);
        await handler.Handle(new ProyectarReembolsoNotaCreditoCommand(Guid.NewGuid(), payload), CancellationToken.None);

        var devolucion = await db.DevolucionesCliente.SingleAsync();
        devolucion.Estado.Should().Be(EstadoDevolucionCliente.Solicitada);
        devolucion.Origen.Should().Be(OrigenDevolucionCliente.NotaCredito);
        devolucion.NotaCreditoId.Should().Be(ncId);
        devolucion.Importe.Should().Be(800m);
        devolucion.ImporteMaximoOrigen.Should().Be(1_000m);
        (await db.EventosProcesados.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Importe_mayor_al_total_lanza_DEV_IMPORTE_EXCEDE_NC()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarReembolsoNotaCreditoHandler(db, new FakeClock(Ahora));

        var act = () => handler.Handle(
            new ProyectarReembolsoNotaCreditoCommand(Guid.NewGuid(), Payload(Guid.NewGuid(), 1_500m)), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_IMPORTE_EXCEDE_NC");
        (await db.DevolucionesCliente.CountAsync()).Should().Be(0);
    }

    private static TesoreriaDbContext CrearDbContext() => new(
        new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase($"tesoreria_test_{Guid.NewGuid()}").Options,
        new FakeEmpresaContext());

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }

    private sealed class FakeClock(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }
}

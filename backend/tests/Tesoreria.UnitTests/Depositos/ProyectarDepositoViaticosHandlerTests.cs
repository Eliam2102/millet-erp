using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.EventListeners;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Depositos;

/// <summary>
/// GI-PR4 (doc 12 §D4/Q3): proyección del depósito esperado por la
/// diferencia negativa de una liquidación de viáticos.
/// </summary>
public sealed class ProyectarDepositoViaticosHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static DepositoViaticosEsperadoPayload Payload(
        Guid? solicitudId = null, decimal monto = 420m) =>
        new(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: Ahora,
            SolicitudViaticosId: solicitudId ?? Guid.NewGuid(),
            EmpleadoId: Guid.NewGuid(),
            MontoEsperado: monto,
            Moneda: "MXN");

    [Fact]
    public async Task Proyecta_expectativa_pendiente_por_solicitud()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarDepositoViaticosHandler(db, new FakeClock(Ahora));
        var payload = Payload(monto: 420m);

        await handler.Handle(
            new ProyectarDepositoViaticosCommand(Guid.NewGuid(), payload), CancellationToken.None);

        var dep = await db.DepositosConfirmacion.SingleAsync();
        dep.SolicitudViaticosId.Should().Be(payload.SolicitudViaticosId);
        dep.MontoEsperado.Should().Be(420m);
        dep.Moneda.Should().Be("MXN");
        dep.Estado.Should().Be(EstadoDepositoConfirmacion.Pendiente);
        dep.DepositoRef.Should().StartWith("VIATICOS ");

        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Reentrega_no_duplica_expectativa()
    {
        using var db = CrearDbContext();
        var handler = new ProyectarDepositoViaticosHandler(db, new FakeClock(Ahora));
        var solicitudId = Guid.NewGuid();

        await handler.Handle(
            new ProyectarDepositoViaticosCommand(Guid.NewGuid(), Payload(solicitudId)), CancellationToken.None);
        await handler.Handle(
            new ProyectarDepositoViaticosCommand(Guid.NewGuid(), Payload(solicitudId)), CancellationToken.None);

        (await db.DepositosConfirmacion.CountAsync()).Should().Be(1);
        (await db.EventosProcesados.CountAsync()).Should().Be(2);
    }

    private static TesoreriaDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase(databaseName: $"tesoreria_test_{Guid.NewGuid()}")
            .Options;
        return new TesoreriaDbContext(options, new FakeEmpresaContext());
    }

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

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Application.Oc.Eventos.Cxp;
using Millet.Compras.Domain.Ports.Tesoreria;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.UnitTests.Oc.Application.Eventos.Cxp;

/// <summary>
/// Cierre del PLATFORM-TODO <c>&lt;TesoreriaEventListenerCompras&gt;</c>:
/// el dispatch de <c>cuentas_por_pagar.factura.pago-aplicado.v1</c>
/// publica el evento in-proc <see cref="PagoFacturaProveedorEvent"/> (que
/// consume el listener F5-PR2, hasta hoy huérfano) con el acumulado por
/// OC calculado por CxP, y deja marca de idempotencia.
/// </summary>
public sealed class FacturaPagoAplicadoCommandTests
{
    [Fact]
    public async Task Publica_notification_con_acumulado_y_marca_idempotencia()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new FacturaPagoAplicadoCommandHandler(db, publisher, NullLogger<FacturaPagoAplicadoCommandHandler>.Instance);

        var ocId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var eventoId = Guid.NewGuid();
        var ocurrido = DateTimeOffset.UtcNow;

        var payload = new FacturaPagoAplicadoPayload(
            EmpresaId: empresaId,
            OcurridoEn: ocurrido,
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: ocId,
            ImportePagadoFactura: 1350m,
            MontoPagadoAcumuladoOc: 1350m);

        await handler.Handle(new FacturaPagoAplicadoCommand(eventoId, payload), CancellationToken.None);

        var n = publisher.Published.OfType<PagoFacturaProveedorEvent>().Should().ContainSingle().Subject;
        n.OrdenCompraId.Should().Be(ocId);
        n.EmpresaId.Should().Be(empresaId);
        n.MontoPagadoAcumulado.Should().Be(1350m);
        n.OcurridoEn.Should().Be(ocurrido);

        var marca = await db.EventosProcesados.SingleAsync();
        marca.EventoId.Should().Be(eventoId);
        marca.EventoTipo.Should().Be("cuentas_por_pagar.factura.pago-aplicado.v1");
    }

    [Fact]
    public void Payload_deserializa_pascal_case_del_wire()
    {
        const string wire = """
            {"EmpresaId": "00000003-0000-0000-0000-000000000001", "OcurridoEn": "2026-07-15T23:00:00+00:00", "FacturaProveedorId": "019f67f4-ad37-779a-bb23-b2209f6b9912", "OrdenCompraId": "019f67f0-2ad0-7ab1-9426-e0d0fe59f9fd", "ImportePagadoFactura": 1350.00, "MontoPagadoAcumuladoOc": 1350.00, "EventType": "cuentas_por_pagar.factura.pago-aplicado.v1"}
            """;

        var payload = System.Text.Json.JsonSerializer.Deserialize<FacturaPagoAplicadoPayload>(
            wire, Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker.JsonOpts);

        payload.Should().NotBeNull();
        payload!.OrdenCompraId.Should().Be(Guid.Parse("019f67f0-2ad0-7ab1-9426-e0d0fe59f9fd"));
        payload.MontoPagadoAcumuladoOc.Should().Be(1350m);
    }

    private static async Task<ComprasDbContext> CreateDbAsync()
    {
        var opts = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase($"compras-pago-aplicado-tests-{Guid.NewGuid():N}")
            .Options;
        var db = new ComprasDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class CapturingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];
        public Task Publish(object notification, CancellationToken cancellationToken = default)
        { Published.Add(notification); return Task.CompletedTask; }
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        { Published.Add(notification!); return Task.CompletedTask; }
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}

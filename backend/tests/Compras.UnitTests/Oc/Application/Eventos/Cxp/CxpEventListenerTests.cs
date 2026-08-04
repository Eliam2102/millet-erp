using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Application.Oc.Eventos.Cxp;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Ports.Cxp;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.UnitTests.Oc.Application.Eventos.Cxp;

/// <summary>
/// Tests del handler que despacha eventos de CxP recibidos por el
/// <c>CxpEventListenerWorker</c> en Compras (PR D — cierre outbound
/// CxP → Compras).
///
/// <para>
/// Foco: verificar el fan-out de notifications por línea de OC, la
/// marca de idempotencia y los casos borde (sin líneas con LineaOcId,
/// re-entrega del mismo eventId).
/// </para>
/// </summary>
public sealed class CxpEventListenerTests
{
    [Fact]
    public async Task FacturaProveedorRegistradaCommand_publica_una_notification_por_linea_oc()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new FacturaProveedorRegistradaCommandHandler(db, publisher, NullLogger<FacturaProveedorRegistradaCommandHandler>.Instance);

        var ocId = Guid.NewGuid();
        var linea1 = Guid.NewGuid();
        var linea2 = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var eventoId = Guid.NewGuid();

        var payload = new FacturaProveedorRegistradaPayload(
            EmpresaId: empresaId,
            OcurridoEn: DateTimeOffset.UtcNow,
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: ocId,
            TotalFactura: 5000m,
            Lineas: [
                new LineaFacturadaPayload(Guid.NewGuid(), linea1, 3m, 1500m),
                new LineaFacturadaPayload(Guid.NewGuid(), linea2, 5m, 3500m),
            ],
            LineasAcumuladasOc: [
                new LineaOcAcumuladaPayload(linea1, 8m),  // hubo 5 previas + 3 actual
                new LineaOcAcumuladaPayload(linea2, 5m),  // primera factura para esta línea
            ]);

        await handler.Handle(new FacturaProveedorRegistradaCommand(eventoId, payload), CancellationToken.None);

        publisher.Published.Should().HaveCount(2);
        var n1 = publisher.Published.OfType<FacturaProveedorRegistradaEvent>().Single(n => n.LineaOrdenCompraId == linea1);
        n1.OrdenCompraId.Should().Be(ocId);
        n1.EmpresaId.Should().Be(empresaId);
        n1.CantidadFacturadaAcumulada.Should().Be(8m);

        var n2 = publisher.Published.OfType<FacturaProveedorRegistradaEvent>().Single(n => n.LineaOrdenCompraId == linea2);
        n2.CantidadFacturadaAcumulada.Should().Be(5m);

        // Persistió la marca de idempotencia.
        var marca = await db.EventosProcesados.SingleAsync();
        marca.EventoId.Should().Be(eventoId);
        marca.EventoTipo.Should().Be("cuentas_por_pagar.factura.registrada.v1");
    }

    [Fact]
    public async Task FacturaProveedorRegistradaCommand_sin_lineas_oc_marca_idempotencia_sin_dispatch()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new FacturaProveedorRegistradaCommandHandler(db, publisher, NullLogger<FacturaProveedorRegistradaCommandHandler>.Instance);

        var payload = new FacturaProveedorRegistradaPayload(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: DateTimeOffset.UtcNow,
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            TotalFactura: 1000m,
            Lineas: [new LineaFacturadaPayload(Guid.NewGuid(), null, 1m, 1000m)],
            LineasAcumuladasOc: []);

        var eventoId = Guid.NewGuid();
        await handler.Handle(new FacturaProveedorRegistradaCommand(eventoId, payload), CancellationToken.None);

        publisher.Published.Should().BeEmpty();
        var marca = await db.EventosProcesados.SingleAsync();
        marca.EventoId.Should().Be(eventoId);
        marca.Observaciones.Should().Contain("Sin líneas con LineaOcId");
    }

    [Fact]
    public async Task FacturaProveedorRegistradaCommand_idempotente_no_duplica_marca()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new FacturaProveedorRegistradaCommandHandler(db, publisher, NullLogger<FacturaProveedorRegistradaCommandHandler>.Instance);

        var eventoId = Guid.NewGuid();
        db.EventosProcesados.Add(new EventoProcesado(eventoId, "cuentas_por_pagar.factura.registrada.v1", "ya estaba"));
        await db.SaveChangesAsync();

        var lineaOc = Guid.NewGuid();
        var payload = new FacturaProveedorRegistradaPayload(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: DateTimeOffset.UtcNow,
            FacturaProveedorId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            TotalFactura: 100m,
            Lineas: [new LineaFacturadaPayload(Guid.NewGuid(), lineaOc, 1m, 100m)],
            LineasAcumuladasOc: [new LineaOcAcumuladaPayload(lineaOc, 1m)]);

        // El worker normalmente cortocircuita antes de invocar el handler
        // (dedupe en el listener). Si llega igual al handler, la marca
        // se mantiene única y el dispatch sigue ocurriendo — la
        // idempotencia real la garantiza el aggregate al recibir el
        // mismo acumulado.
        await handler.Handle(new FacturaProveedorRegistradaCommand(eventoId, payload), CancellationToken.None);

        var marcas = await db.EventosProcesados.CountAsync();
        marcas.Should().Be(1);
    }

    [Fact]
    public void JsonOpts_deserializa_payload_pascal_case_del_wire()
    {
        // Payload REAL del outbox de CxP (OutboxSaveChangesInterceptor
        // serializa PascalCase). Regresión del incidente 2026-07-15:
        // JsonOpts solo con camelCase (case-sensitive) dejaba
        // LineasAcumuladasOc en null y los Guid en Guid.Empty → NRE en
        // el handler y mensaje a dead-letter.
        const string wire = """
            {"Lineas": [{"Importe": 1350.00, "Cantidad": 9, "LineaOcId": "019f67f0-2ad8-7217-ac02-4db3899796e5", "LineaFacturaId": "019f67f4-ad37-7216-b866-fb00d0d7a1fd"}], "EmpresaId": "00000003-0000-0000-0000-000000000001", "EventType": "cuentas_por_pagar.factura.registrada.v1", "OcurridoEn": "2026-07-15T22:45:16.2150721+00:00", "TotalFactura": 1350.00, "OrdenCompraId": "019f67f0-2ad0-7ab1-9426-e0d0fe59f9fd", "FacturaProveedorId": "019f67f4-ad37-779a-bb23-b2209f6b9912", "LineasAcumuladasOc": [{"LineaOcId": "019f67f0-2ad8-7217-ac02-4db3899796e5", "CantidadAcumulada": 9}]}
            """;

        var payload = System.Text.Json.JsonSerializer.Deserialize<FacturaProveedorRegistradaPayload>(
            wire, Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker.JsonOpts);

        payload.Should().NotBeNull();
        payload!.OrdenCompraId.Should().Be(Guid.Parse("019f67f0-2ad0-7ab1-9426-e0d0fe59f9fd"));
        payload.EmpresaId.Should().NotBe(Guid.Empty);
        payload.LineasAcumuladasOc.Should().NotBeNull();
        payload.LineasAcumuladasOc.Should().ContainSingle()
            .Which.CantidadAcumulada.Should().Be(9m);
    }

    private static async Task<ComprasDbContext> CreateDbAsync()
    {
        var opts = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase($"compras-cxp-listener-tests-{Guid.NewGuid():N}")
            .Options;
        var db = new ComprasDbContext(opts, new BypassedEmpresaContext());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private sealed class CapturingPublisher : IPublisher
    {
        public List<object> Published { get; } = [];

        public Task Publish(object notification, CancellationToken cancellationToken = default)
        {
            Published.Add(notification);
            return Task.CompletedTask;
        }

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            Published.Add(notification!);
            return Task.CompletedTask;
        }
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}

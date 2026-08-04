using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.UnitTests.Application.Almacen;

/// <summary>
/// GAP-5 (verificación e2e 2026-07-15): el dispatch de
/// <c>almacen.oc_devolucion.registrada.v1</c> calcula el acumulado
/// ajustado (recibido − devuelto) y publica el evento in-proc
/// <see cref="OcDevolucionRegistradaEvent"/> que consume el listener
/// F5-PR2 (hasta hoy huérfano). Foco: fan-out, marca de idempotencia y
/// casos borde (sin OC de origen, línea sin LineaOcId, re-entrega).
/// </summary>
public sealed class OcDevolucionEnAlmacenHandlerTests
{
    [Fact]
    public async Task Sin_orden_compra_origen_solo_marca_idempotencia()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);
        var eventoId = Guid.NewGuid();

        await handler.Handle(new OcDevolucionEnAlmacenCommand(eventoId, Payload(ordenCompraId: null,
            Linea(lineaOcId: Guid.NewGuid(), cantidad: 5m))), CancellationToken.None);

        publisher.Published.Should().BeEmpty();
        var marca = await db.EventosProcesados.SingleAsync();
        marca.EventoId.Should().Be(eventoId);
        marca.EventoTipo.Should().Be("almacen.oc_devolucion.registrada.v1");
    }

    [Fact]
    public async Task Linea_sin_linea_oc_id_no_publica_pero_marca()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);

        await handler.Handle(new OcDevolucionEnAlmacenCommand(Guid.NewGuid(), Payload(
            ordenCompraId: Guid.NewGuid(),
            Linea(lineaOcId: null, cantidad: 5m))), CancellationToken.None);

        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Idempotente_no_reprocesa_mismo_evento()
    {
        await using var db = await CreateDbAsync();
        var publisher = new CapturingPublisher();
        var handler = new OcDevolucionEnAlmacenHandler(db, publisher, NullLogger<OcDevolucionEnAlmacenHandler>.Instance);
        var eventoId = Guid.NewGuid();

        db.EventosProcesados.Add(new Millet.Compras.Domain.Idempotencia.EventoProcesado(
            eventoId, "almacen.oc_devolucion.registrada.v1", "ya estaba"));
        await db.SaveChangesAsync();

        await handler.Handle(new OcDevolucionEnAlmacenCommand(eventoId, Payload(
            ordenCompraId: Guid.NewGuid(),
            Linea(lineaOcId: Guid.NewGuid(), cantidad: 5m))), CancellationToken.None);

        publisher.Published.Should().BeEmpty();
        (await db.EventosProcesados.CountAsync()).Should().Be(1);
    }

    private static LineaDevolucionAlmacenPayload Linea(Guid? lineaOcId, decimal cantidad) =>
        new(
            LineaDevolucionId: Guid.NewGuid(),
            LineaRecepcionOrigenId: Guid.NewGuid(),
            LineaOcId: lineaOcId,
            ArticuloId: Guid.NewGuid(),
            UnidadMedida: "PZA",
            Cantidad: cantidad,
            CostoUnitarioMxn: 25m,
            MontoTotalMxn: cantidad * 25m);

    private static OcDevolucionRegistradaAlmacenPayload Payload(
        Guid? ordenCompraId, params LineaDevolucionAlmacenPayload[] lineas) =>
        new(
            EmpresaId: Guid.NewGuid(),
            OcurridoEn: DateTimeOffset.UtcNow,
            DevolucionId: Guid.NewGuid(),
            FolioMovimiento: "M-DEV2026-000099",
            ProveedorId: Guid.NewGuid(),
            RecepcionOrigenId: Guid.NewGuid(),
            FacturaProveedorOrigenId: null,
            OrdenCompraOrigenId: ordenCompraId,
            Motivo: "Test",
            MontoTotalMxn: lineas.Sum(l => l.MontoTotalMxn),
            Lineas: lineas);

    private static async Task<ComprasDbContext> CreateDbAsync()
    {
        var opts = new DbContextOptionsBuilder<ComprasDbContext>()
            .UseInMemoryDatabase($"compras-devolucion-listener-tests-{Guid.NewGuid():N}")
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

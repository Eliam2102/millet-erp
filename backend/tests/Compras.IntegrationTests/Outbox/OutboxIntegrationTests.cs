using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.IntegrationTests.Outbox;

/// <summary>
/// Tests integration del outbox de eventos de integración (F6-PR1):
/// verifica que <see cref="IIntegrationEventPublisher"/> encola al
/// buffer scoped y el interceptor drena al SaveChanges, persistiendo
/// las filas en <c>compras.integration_events_outbox</c> dentro de la
/// misma transacción EF (atomicidad ADR-0009).
/// </summary>
public class OutboxIntegrationTests : IClassFixture<StubsWebApplicationFactory>
{
    private readonly StubsWebApplicationFactory _factory;

    public OutboxIntegrationTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Publish_Y_SaveChanges_PersisteFilaEnOutbox_ConPayloadJson()
    {
        var ev = new TestRequisicionAutorizadaIntegrationEvent(
            EmpresaId: Guid.CreateVersion7(),
            OcurridoEn: DateTimeOffset.UtcNow,
            RequisicionId: Guid.CreateVersion7(),
            Folio: "MID2026-000001");

        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        // Usar una entidad existente que ya este tracked para forzar
        // SaveChanges a hacer trabajo real (no no-op si no hay nada
        // tracked además del outbox row). Un MotivoRechazo seed sirve
        // como "reload" no-op (no muta nada). Mejor: insertar una fila
        // dummy via un command? Más simple: agregar directamente al
        // OcBorradorStub stub y verificar que el outbox row también
        // queda en la misma TX.
        //
        // Aún más simple: solo publicar y SaveChanges. El interceptor
        // se ejecuta en cualquier SaveChanges (incluso no-op).
        await publisher.PublishAsync(ev, CancellationToken.None);
        await db.SaveChangesAsync();

        // Verificar la fila en outbox.
        var fila = await db.OutboxEntries
            .AsNoTracking()
            .Where(e => e.EventType == ev.EventType)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(fila);
        Assert.Equal(ev.EventType, fila!.EventType);
        Assert.Equal(ev.EmpresaId, fila.IntegrationEmpresaId);
        Assert.Null(fila.PublishedAt);
        Assert.Equal(0, fila.Attempts);

        // El payload deserializa al evento original.
        var deserialized = JsonSerializer.Deserialize<TestRequisicionAutorizadaIntegrationEvent>(fila.Payload);
        Assert.NotNull(deserialized);
        Assert.Equal(ev.RequisicionId, deserialized!.RequisicionId);
        Assert.Equal(ev.Folio, deserialized.Folio);
    }

    [Fact]
    public async Task Publish_SinSaveChanges_NoPersisteFila()
    {
        var ev = new TestRequisicionAutorizadaIntegrationEvent(
            EmpresaId: Guid.CreateVersion7(),
            OcurridoEn: DateTimeOffset.UtcNow,
            RequisicionId: Guid.CreateVersion7(),
            Folio: "MID2026-NOSAVE");

        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        await publisher.PublishAsync(ev, CancellationToken.None);
        // SIN SaveChanges → buffer no se drena → no hay fila persistida.

        // Buscar en otra scope (DbContext nuevo) que la fila no exista.
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var verifyEmpresa = verifyScope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var verifyBypass = verifyEmpresa.Bypass();

        var fila = await verifyDb.OutboxEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventType == ev.EventType
                && e.IntegrationEmpresaId == ev.EmpresaId);

        Assert.Null(fila);
    }

    [Fact]
    public async Task Publish_MultiplesEventos_PersisteTodasLasFilas()
    {
        var empresaId = Guid.CreateVersion7();
        var marker = $"MULTI-{Guid.NewGuid():N}";

        var ev1 = new TestRequisicionAutorizadaIntegrationEvent(
            empresaId, DateTimeOffset.UtcNow, Guid.CreateVersion7(), $"{marker}-1");
        var ev2 = new TestRequisicionAutorizadaIntegrationEvent(
            empresaId, DateTimeOffset.UtcNow, Guid.CreateVersion7(), $"{marker}-2");
        var ev3 = new TestRequisicionAutorizadaIntegrationEvent(
            empresaId, DateTimeOffset.UtcNow, Guid.CreateVersion7(), $"{marker}-3");

        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        await publisher.PublishAsync(ev1, CancellationToken.None);
        await publisher.PublishAsync(ev2, CancellationToken.None);
        await publisher.PublishAsync(ev3, CancellationToken.None);
        await db.SaveChangesAsync();

        var filas = await db.OutboxEntries
            .AsNoTracking()
            .Where(e => e.IntegrationEmpresaId == empresaId)
            .ToListAsync();

        Assert.Equal(3, filas.Count);
        Assert.All(filas, f => Assert.Null(f.PublishedAt));
    }

    [Fact]
    public async Task Publish_ObjectNoIntegrationEvent_Throws()
    {
        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            publisher.PublishAsync(new { Foo = "bar" }, CancellationToken.None));
    }

    /// <summary>
    /// Evento de prueba que extiende <see cref="IntegrationEvent"/>.
    /// Vive aquí (en tests) porque F6-PR1 no wirea ningún tipo concreto
    /// de Compras todavía — eso es F6-PR3.
    /// </summary>
    private sealed record TestRequisicionAutorizadaIntegrationEvent(
        Guid EmpresaId,
        DateTimeOffset OcurridoEn,
        Guid RequisicionId,
        string Folio)
        : IntegrationEvent("test.compras.requisicion.autorizada.v1", EmpresaId, OcurridoEn);
}

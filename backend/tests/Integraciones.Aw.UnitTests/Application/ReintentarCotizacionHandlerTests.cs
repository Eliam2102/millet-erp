using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Integraciones.Aw.Application.Commands.ReintentarCotizacion;
using Millet.Integraciones.Aw.Application.IntegrationEvents;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Domain.Exceptions;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.UnitTests.Application;

/// <summary>
/// Tests unit del <see cref="ReintentarCotizacionHandler"/>. Usa
/// in-memory EF (Sqlite) + fake IntegrationEventPublisher para verificar
/// que el handler llama al método de dominio y emite el evento correcto.
/// </summary>
public sealed class ReintentarCotizacionHandlerTests
{
    [Fact]
    public async Task Reintentar_DesdeFailedDrop_TransicionaSubmitted_EmiteEvento()
    {
        var (handler, db, publisher, _) = await BuildAsync();
        var entidad = SeedEntidad(EstadoEntidad.FailedDrop);
        db.EntidadesExternas.Add(entidad);
        await db.SaveChangesAsync();

        var response = await handler.Handle(
            new ReintentarCotizacionCommand(entidad.Id, "operador X cambió decisión"),
            CancellationToken.None);

        response.Id.Should().Be(entidad.Id);
        response.QuoteReference.Should().Be(entidad.ReferenciaExterna);
        response.Estado.Should().Be(EstadoEntidad.Submitted);

        var afterSave = await db.EntidadesExternas.FindAsync(entidad.Id);
        afterSave!.Estado.Should().Be(EstadoEntidad.Submitted);
        afterSave.RetryCount.Should().Be((short)0);
        afterSave.LastError.Should().BeNull();

        publisher.Published.Should().ContainSingle()
            .Which.Should().BeOfType<AwCotizacionRecibida>();
    }

    [Fact]
    public async Task Reintentar_NoExiste_LanzaEntityNotFoundException()
    {
        var (handler, _, _, _) = await BuildAsync();

        var act = () => handler.Handle(
            new ReintentarCotizacionCommand(Guid.NewGuid(), null),
            CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>()
            .Where(e => e.Code == "AW_COTIZACION_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Reintentar_DesdeSubmitted_LanzaInvalidStateTransition()
    {
        var (handler, db, _, _) = await BuildAsync();
        var entidad = SeedEntidad(EstadoEntidad.Submitted);
        db.EntidadesExternas.Add(entidad);
        await db.SaveChangesAsync();

        var act = () => handler.Handle(
            new ReintentarCotizacionCommand(entidad.Id, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidStateTransitionException>();
    }

    [Fact]
    public async Task Reintentar_DesdeCorrelated_LanzaInvalidStateTransition()
    {
        var (handler, db, _, _) = await BuildAsync();
        var entidad = SeedEntidad(EstadoEntidad.Correlated);
        db.EntidadesExternas.Add(entidad);
        await db.SaveChangesAsync();

        var act = () => handler.Handle(
            new ReintentarCotizacionCommand(entidad.Id, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidStateTransitionException>();
    }

    // ─── Helpers ───

    private static EntidadExterna SeedEntidad(EstadoEntidad target)
    {
        var e = new EntidadExterna(
            id: Guid.NewGuid(),
            tipoEntidad: TipoEntidad.Cotizacion,
            referenciaExterna: $"Q-{Guid.NewGuid():N}".Substring(0, 12),
            empresaId: Guid.NewGuid(),
            payloadOriginal: "{}",
            ediContent: "EDI",
            submittedBySpnId: null,
            submittedAt: DateTimeOffset.UtcNow,
            sucursal: "CIR");
        AdvanceTo(e, target);
        return e;
    }

    private static void AdvanceTo(EntidadExterna e, EstadoEntidad target)
    {
        if (target is EstadoEntidad.Submitted) return;
        if (target is EstadoEntidad.Correlated)
        {
            // PR #201: direct callback model.
            e.MarcarCorrelacionadaDirectamente(1L, DateTimeOffset.UtcNow);
            return;
        }
        if (target is EstadoEntidad.FailedDrop)
        {
            e.IncrementarRetry("err1", "transient");
            e.MarcarDropFalladoTerminal("max", "permanent");
            return;
        }
        if (target is EstadoEntidad.FailedCorrelation)
        {
            // PR #201: direct callback model.
            e.MarcarCorrelacionFallidaDirectamente(
                errorCodes: ["1555"],
                errorMessage: "test rejection",
                failedAt: DateTimeOffset.UtcNow);
            return;
        }
        if (target is EstadoEntidad.ManuallyResolved)
        {
            e.MarcarDropFalladoTerminal("x", "permanent");
            e.MarcarResueltoManual("nota inicial", Guid.NewGuid());
            return;
        }
        throw new InvalidOperationException($"Estado {target} no soportado.");
    }

    private static async Task<(
        ReintentarCotizacionHandler Handler,
        IntegracionesAwDbContext Db,
        FakePublisher Publisher,
        FakeClock Clock)> BuildAsync()
    {
        var db = await InMemoryDb.CreateAsync();
        var publisher = new FakePublisher();
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var handler = new ReintentarCotizacionHandler(
            db, publisher, new NoOpAgentRealtime(),
            clock, NullLogger<ReintentarCotizacionHandler>.Instance);
        return (handler, db, publisher, clock);
    }

    private sealed class NoOpAgentRealtime : IAgentRealtimePublisher
    {
        public Task PublishCotizacionActualizadaAsync(EntidadExterna entidad, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Published { get; } = new();
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Published.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset n) { UtcNow = n; }
        public DateTimeOffset UtcNow { get; set; }
    }
}

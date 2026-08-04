using Microsoft.Extensions.Logging.Abstractions;
using Millet.Integraciones.Aw.Application.Commands.MarcarResueltoManual;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Domain.Exceptions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Application;

public sealed class MarcarResueltoManualHandlerTests
{
    [Fact]
    public async Task DesdeFailedDrop_TransicionaManuallyResolved_PersisteNotaYOperador()
    {
        var operadorId = Guid.NewGuid();
        var (handler, db) = await BuildAsync(operadorId);
        var entidad = NewEntidadAtState(EstadoEntidad.FailedDrop);
        db.EntidadesExternas.Add(entidad);
        await db.SaveChangesAsync();

        var response = await handler.Handle(
            new MarcarResueltoManualCommand(entidad.Id, "cliente acordó otra cotización"),
            CancellationToken.None);

        response.Estado.Should().Be(EstadoEntidad.ManuallyResolved);
        response.ResolutionNote.Should().Contain("cliente acordó otra cotización");
        response.ResolutionNote.Should().Contain(operadorId.ToString());
    }

    [Fact]
    public async Task DesdeFailedCorrelation_OK()
    {
        var (handler, db) = await BuildAsync(Guid.NewGuid());
        var entidad = NewEntidadAtState(EstadoEntidad.FailedCorrelation);
        db.EntidadesExternas.Add(entidad);
        await db.SaveChangesAsync();

        var response = await handler.Handle(
            new MarcarResueltoManualCommand(entidad.Id, "no se va a corregir"),
            CancellationToken.None);

        response.Estado.Should().Be(EstadoEntidad.ManuallyResolved);
    }

    [Fact]
    public async Task DesdeSubmitted_LanzaInvalidStateTransition()
    {
        var (handler, db) = await BuildAsync(Guid.NewGuid());
        var entidad = NewEntidadAtState(EstadoEntidad.Submitted);
        db.EntidadesExternas.Add(entidad);
        await db.SaveChangesAsync();

        var act = () => handler.Handle(
            new MarcarResueltoManualCommand(entidad.Id, "intento prematuro"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidStateTransitionException>();
    }

    // PR #201 retiró AwaitingCorrelation del state machine activo — el
    // único estado "in-flight" hoy es Submitted (pre-drop). El test
    // `DesdeSubmitted_LanzaInvalidStateTransition` arriba ya cubre el
    // caso de rechazar resolución manual en flight.

    [Fact]
    public async Task NotaVacia_LanzaBusinessRuleException()
    {
        var (handler, _) = await BuildAsync(Guid.NewGuid());

        var act = () => handler.Handle(
            new MarcarResueltoManualCommand(Guid.NewGuid(), "  "),
            CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Subject.First();
        ex.Code.Should().Be("AW_NOTA_REQUERIDA");
    }

    [Fact]
    public async Task NoExisteEntidad_LanzaEntityNotFoundException()
    {
        var (handler, _) = await BuildAsync(Guid.NewGuid());

        var act = () => handler.Handle(
            new MarcarResueltoManualCommand(Guid.NewGuid(), "nota"),
            CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task SinUsuarioActual_LanzaForbidden()
    {
        // Operador null = sin auth (ej. SP que no debería poder hacer admin actions).
        var (handler, db) = await BuildAsync(operadorId: null);
        var entidad = NewEntidadAtState(EstadoEntidad.FailedDrop);
        db.EntidadesExternas.Add(entidad);
        await db.SaveChangesAsync();

        var act = () => handler.Handle(
            new MarcarResueltoManualCommand(entidad.Id, "nota"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.Code == "AW_OPERADOR_REQUERIDO");
    }

    // ─── Helpers ───

    private static EntidadExterna NewEntidadAtState(EstadoEntidad target)
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
        if (target is EstadoEntidad.Submitted) return e;
        if (target is EstadoEntidad.FailedDrop)
        {
            e.MarcarDropFalladoTerminal("max", "permanent");
            return e;
        }
        if (target is EstadoEntidad.FailedCorrelation)
        {
            // PR #201: el camino a FailedCorrelation es directo desde
            // Submitted via MarcarCorrelacionFallidaDirectamente (callback
            // del drop service reportando outcome=failed).
            e.MarcarCorrelacionFallidaDirectamente(
                errorCodes: ["1555"],
                errorMessage: "test rejection",
                failedAt: DateTimeOffset.UtcNow);
            return e;
        }
        throw new InvalidOperationException($"Estado {target} no soportado.");
    }

    private static async Task<(MarcarResueltoManualHandler Handler, Infrastructure.Persistence.IntegracionesAwDbContext Db)>
        BuildAsync(Guid? operadorId)
    {
        var db = await InMemoryDb.CreateAsync();
        var handler = new MarcarResueltoManualHandler(
            db,
            new FakeUserContext(operadorId),
            new NoOpAgentRealtime(),
            new FakeClock(DateTimeOffset.UtcNow),
            NullLogger<MarcarResueltoManualHandler>.Instance);
        return (handler, db);
    }

    private sealed class NoOpAgentRealtime : Millet.Integraciones.Aw.Application.Ports.IAgentRealtimePublisher
    {
        public Task PublishCotizacionActualizadaAsync(EntidadExterna entidad, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class FakeUserContext : ICurrentUserContext
    {
        public FakeUserContext(Guid? userId) { UserId = userId; }
        public Guid? UserId { get; }
        public string? UserName => null;
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset n) { UtcNow = n; }
        public DateTimeOffset UtcNow { get; set; }
    }
}

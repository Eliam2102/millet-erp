using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR;
using Millet.Api.Hubs;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Api.IntegrationTests.Hubs;

/// <summary>
/// Unit tests del wiring de métricas del CollaborationHub (Sprint 3).
/// Inyectan un <see cref="IHubContext{ComprasHub}"/> no-op (los broadcasts
/// no nos interesan aquí) y un <see cref="ComprasHubMeter"/> real, y usan
/// <see cref="MeterListener"/> para capturar las measurements.
///
/// <para>
/// Vive en el proyecto de IntegrationTests por simplicidad — no requiere
/// scaffolding de <c>WebApplicationFactory</c> y agregar otro proyecto
/// solo para esta clase sería overhead. Es deterministic: no comparte
/// <c>WebApplicationFactory</c> con otras suites, así que no compite con
/// tests paralelos.
/// </para>
/// </summary>
public class ComprasHubMeterTests
{
    [Fact]
    public async Task Track_Incrementa_SoftLockTracked_Con_Tags_Correctos()
    {
        var (manager, meter, listener) = BuildSut();
        using (listener)
        using (meter)
        {
            await manager.TrackAsync(
                empresaId: TestEmpresaId,
                userId: TestUserId,
                userNombre: "Test User",
                connectionId: "conn-1",
                entidad: "Requisicion",
                entidadId: TestRequisicionId,
                modo: SoftLockModo.Editing,
                cancellationToken: CancellationToken.None);

            var tracked = listener.MeasurementsFor("softlock.tracked");
            Assert.Single(tracked);
            Assert.Equal(1L, tracked[0].Value);
            Assert.Equal("Requisicion", tracked[0].Tags["compras.hub.entidad"]);
            Assert.Equal("Editing", tracked[0].Tags["compras.hub.modo"]);
            Assert.Equal(TestEmpresaId, tracked[0].Tags["compras.hub.empresa.id"]);
        }
    }

    [Fact]
    public async Task Release_Incrementa_SoftLockReleased_Con_Tags_De_La_Entry_Removida()
    {
        var (manager, meter, listener) = BuildSut();
        using (listener)
        using (meter)
        {
            await manager.TrackAsync(
                TestEmpresaId, TestUserId, "Test User", "conn-1",
                "Requisicion", TestRequisicionId, SoftLockModo.Viewing,
                CancellationToken.None);

            await manager.ReleaseAsync("conn-1", CancellationToken.None);

            var released = listener.MeasurementsFor("softlock.released");
            Assert.Single(released);
            Assert.Equal(1L, released[0].Value);
            Assert.Equal("Viewing", released[0].Tags["compras.hub.modo"]);
        }
    }

    [Fact]
    public async Task Release_Sin_Entry_Previa_No_Emite_Medicion()
    {
        var (manager, meter, listener) = BuildSut();
        using (listener)
        using (meter)
        {
            await manager.ReleaseAsync("conn-inexistente", CancellationToken.None);

            Assert.Empty(listener.MeasurementsFor("softlock.released"));
        }
    }

    private static readonly Guid TestEmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TestUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestRequisicionId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static (SoftLockManager Manager, ComprasHubMeter Meter, MeasurementCapture Listener) BuildSut()
    {
        var meter = new ComprasHubMeter();
        var listener = new MeasurementCapture(ComprasHubMeter.Name);
        var manager = new SoftLockManager(
            new NoOpHubContext(),
            new SystemClock(),
            meter);
        return (manager, meter, listener);
    }

    /// <summary>
    /// Captura measurements de un Meter por nombre. Suscribe a todos los
    /// instruments cuyo Meter.Name coincida.
    /// </summary>
    private sealed class MeasurementCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Dictionary<string, List<Measurement>> _measurements = new();
        private readonly object _lock = new();

        public MeasurementCapture(string meterName)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (instrument.Meter.Name == meterName)
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>(OnLongMeasurement);
            _listener.Start();
        }

        public List<Measurement> MeasurementsFor(string instrumentName)
        {
            lock (_lock)
            {
                return _measurements.TryGetValue(instrumentName, out var list)
                    ? list.ToList()
                    : new List<Measurement>();
            }
        }

        private void OnLongMeasurement(
            Instrument instrument,
            long measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
        {
            var tagsDict = new Dictionary<string, object?>(tags.Length);
            foreach (var t in tags)
            {
                tagsDict[t.Key] = t.Value;
            }
            lock (_lock)
            {
                if (!_measurements.TryGetValue(instrument.Name, out var list))
                {
                    list = new List<Measurement>();
                    _measurements[instrument.Name] = list;
                }
                list.Add(new Measurement(measurement, tagsDict));
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed record Measurement(long Value, IReadOnlyDictionary<string, object?> Tags);

    /// <summary>
    /// IHubContext stub: <c>SendAsync</c> no hace nada. El manager solo
    /// llama a <c>Clients.Group(...).SendAsync(...)</c> para broadcastear
    /// presence; este test no se preocupa por el broadcast en sí.
    /// </summary>
    private sealed class NoOpHubContext : IHubContext<ComprasHub>
    {
        public IHubClients Clients { get; } = new NoOpHubClients();
        public IGroupManager Groups { get; } = new NoOpGroupManager();
    }

    private sealed class NoOpHubClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NoOpClientProxy();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NoOpClientProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

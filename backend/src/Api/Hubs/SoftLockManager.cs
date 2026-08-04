using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.SignalR;
using Millet.SharedKernel.Application;

namespace Millet.Api.Hubs;

/// <summary>
/// Implementación in-memory de <see cref="ISoftLockManager"/>.
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed por
/// <c>ConnectionId</c> — una conexión = una entry. Las consultas por
/// recurso (<see cref="GetForResource"/>) son linear scan sobre el
/// dictionary; aceptable para el rango esperado (30-50 conexiones).
///
/// Singleton: el ciclo de vida es el del proceso del hub. Azure SignalR
/// Service hace el routing entre instancias del backplane, pero el estado
/// per-instancia diverge hasta el siguiente heartbeat — aceptable para
/// awareness colaborativo.
/// </summary>
public sealed class SoftLockManager : ISoftLockManager
{
    private const string PresenceEventName = "userPresence";

    private readonly ConcurrentDictionary<string, SoftLockEntry> _entries = new();
    private readonly IHubContext<ComprasHub> _hubContext;
    private readonly IClock _clock;
    private readonly ComprasHubMeter _meter;

    public SoftLockManager(IHubContext<ComprasHub> hubContext, IClock clock, ComprasHubMeter meter)
    {
        _hubContext = hubContext;
        _clock = clock;
        _meter = meter;
    }

    public async Task TrackAsync(
        Guid empresaId,
        Guid userId,
        string userNombre,
        string connectionId,
        string entidad,
        Guid entidadId,
        SoftLockModo modo,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        // Si ya había una entry para esta conexión en otro recurso, hay
        // que notificar al grupo del recurso anterior que el usuario se fue.
        var previous = _entries.TryGetValue(connectionId, out var existing) ? existing : null;
        var sinceUtc = (previous is not null
                        && previous.Entidad == entidad
                        && previous.EntidadId == entidadId)
            ? previous.SinceUtc
            : now;

        var entry = new SoftLockEntry(
            Entidad: entidad,
            EntidadId: entidadId,
            EmpresaId: empresaId,
            UserId: userId,
            UserNombre: userNombre,
            ConnectionId: connectionId,
            Modo: modo,
            SinceUtc: sinceUtc,
            LastSeenUtc: now);

        _entries[connectionId] = entry;

        _meter.SoftLockTracked.Add(1,
            new KeyValuePair<string, object?>("compras.hub.empresa.id", empresaId),
            new KeyValuePair<string, object?>("compras.hub.entidad", entidad),
            new KeyValuePair<string, object?>("compras.hub.modo", modo.ToString()));

        if (previous is not null
            && (previous.Entidad != entidad || previous.EntidadId != entidadId))
        {
            await BroadcastPresenceAsync(
                previous.EmpresaId, previous.Entidad, previous.EntidadId, cancellationToken);
        }

        await BroadcastPresenceAsync(empresaId, entidad, entidadId, cancellationToken);
    }

    public Task RefreshAsync(string connectionId, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(connectionId, out var existing))
        {
            _entries[connectionId] = existing with { LastSeenUtc = _clock.UtcNow };
        }
        return Task.CompletedTask;
    }

    public async Task ReleaseAsync(string connectionId, CancellationToken cancellationToken)
    {
        if (_entries.TryRemove(connectionId, out var removed))
        {
            _meter.SoftLockReleased.Add(1,
                new KeyValuePair<string, object?>("compras.hub.empresa.id", removed.EmpresaId),
                new KeyValuePair<string, object?>("compras.hub.entidad", removed.Entidad),
                new KeyValuePair<string, object?>("compras.hub.modo", removed.Modo.ToString()));

            await BroadcastPresenceAsync(
                removed.EmpresaId, removed.Entidad, removed.EntidadId, cancellationToken);
        }
    }

    public IReadOnlyList<SoftLockEntry> GetForResource(Guid empresaId, string entidad, Guid entidadId)
    {
        return _entries.Values
            .Where(e => e.EmpresaId == empresaId
                        && e.Entidad == entidad
                        && e.EntidadId == entidadId)
            .ToList();
    }

    public IReadOnlyList<SoftLockEntry> Snapshot() => _entries.Values.ToList();

    private async Task BroadcastPresenceAsync(
        Guid empresaId, string entidad, Guid entidadId, CancellationToken cancellationToken)
    {
        var users = GetForResource(empresaId, entidad, entidadId)
            .Select(e => new
            {
                userId = e.UserId,
                userNombre = e.UserNombre,
                modo = e.Modo.ToString(),
                sinceUtc = e.SinceUtc,
                lastSeenUtc = e.LastSeenUtc,
            })
            .ToList();

        var payload = new
        {
            entidad,
            entidadId,
            users,
        };

        await _hubContext.Clients
            .Group(ComprasHub.GroupForEmpresa(empresaId))
            .SendAsync(PresenceEventName, payload, cancellationToken);
    }
}

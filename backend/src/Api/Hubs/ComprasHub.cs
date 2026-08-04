using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Millet.SharedKernel.Application;

namespace Millet.Api.Hubs;

/// <summary>
/// Hub de colaboración del módulo Compras (CollaborationHub, ADR-0001
/// + ADR-0012 Capa 2). Provee:
///
/// <list type="bullet">
///   <item>Aislamiento por empresa: cada conexión se agrega al grupo
///         <c>empresa:{id}</c> derivado del claim <c>current_empresa_id</c>
///         del JWT. Los broadcasts de <see cref="SoftLockManager"/> se
///         hacen por grupo — un usuario de la empresa A nunca recibe
///         eventos de la B.</item>
///   <item>Soft locks de Capa 2: <c>ViewingResource</c>, <c>EditingResource</c>,
///         <c>LeaveResource</c>, <c>Heartbeat</c>, <c>GetPresence</c> —
///         delegados al <see cref="ISoftLockManager"/> singleton.</item>
/// </list>
///
/// Sprint 1 entregó skeleton + auth + grupos. Sprint 2 conecta los
/// métodos al manager real. La protección final contra lost updates
/// sigue viviendo en Capa 1 (<c>Version</c>); esta capa es awareness.
/// </summary>
[Authorize]
public sealed class ComprasHub : Hub
{
    /// <summary>Prefijo del nombre de grupo por empresa.</summary>
    public const string EmpresaGroupPrefix = "empresa:";

    /// <summary>Construye el nombre de grupo para una empresa dada.</summary>
    public static string GroupForEmpresa(Guid empresaId) => EmpresaGroupPrefix + empresaId;

    private readonly ISoftLockManager _softLocks;
    private readonly ComprasHubMeter _meter;

    public ComprasHub(ISoftLockManager softLocks, ComprasHubMeter meter)
    {
        _softLocks = softLocks;
        _meter = meter;
    }

    public override async Task OnConnectedAsync()
    {
        if (!TryGetEmpresaId(out var empresaId))
        {
            // Conexiones sin empresa no tienen sentido en Compras (la
            // empresa define el grupo de broadcast). El cliente debe
            // reconectar después de seleccionar empresa.
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupForEmpresa(empresaId));
        _meter.HubConnectionsDelta.Add(1,
            new KeyValuePair<string, object?>("compras.hub.empresa.id", empresaId));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Releasa la entry — el manager broadcastea userPresence al grupo
        // sin el usuario. Cubre el caso "cerró el browser sin LeaveResource".
        await _softLocks.ReleaseAsync(Context.ConnectionId, CancellationToken.None);

        if (TryGetEmpresaId(out var empresaId))
        {
            _meter.HubConnectionsDelta.Add(-1,
                new KeyValuePair<string, object?>("compras.hub.empresa.id", empresaId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>El usuario está viendo un recurso (sin editarlo).</summary>
    public Task ViewingResource(string entidad, Guid id) =>
        TrackAsync(entidad, id, SoftLockModo.Viewing);

    /// <summary>El usuario empezó a editar — el FE lo llama al focus de un input.</summary>
    public Task EditingResource(string entidad, Guid id) =>
        TrackAsync(entidad, id, SoftLockModo.Editing);

    /// <summary>El usuario salió del recurso explícitamente (cerró pantalla).</summary>
    public Task LeaveResource() =>
        _softLocks.ReleaseAsync(Context.ConnectionId, Context.ConnectionAborted);

    /// <summary>Heartbeat silencioso: solo refresca el TTL.</summary>
    public Task Heartbeat() =>
        _softLocks.RefreshAsync(Context.ConnectionId, Context.ConnectionAborted);

    /// <summary>
    /// Snapshot inicial de presencia para un recurso. Se invoca tras
    /// conectar para que el FE pinte el indicador sin esperar al primer
    /// push del manager.
    /// </summary>
    public IReadOnlyList<UserPresenceDto> GetPresence(string entidad, Guid id)
    {
        if (!TryGetEmpresaId(out var empresaId))
        {
            return Array.Empty<UserPresenceDto>();
        }

        return _softLocks.GetForResource(empresaId, entidad, id)
            .Select(e => new UserPresenceDto(
                UserId: e.UserId,
                UserNombre: e.UserNombre,
                Modo: e.Modo,
                SinceUtc: e.SinceUtc,
                LastSeenUtc: e.LastSeenUtc))
            .ToList();
    }

    private async Task TrackAsync(string entidad, Guid id, SoftLockModo modo)
    {
        if (!TryGetEmpresaId(out var empresaId)
            || !TryGetUserId(out var userId))
        {
            return;
        }

        var nombre = Context.User?.FindFirst(MilletClaimTypes.Name)?.Value ?? string.Empty;

        await _softLocks.TrackAsync(
            empresaId: empresaId,
            userId: userId,
            userNombre: nombre,
            connectionId: Context.ConnectionId,
            entidad: entidad,
            entidadId: id,
            modo: modo,
            cancellationToken: Context.ConnectionAborted);
    }

    private bool TryGetEmpresaId(out Guid empresaId)
    {
        var raw = Context.User?.FindFirst(MilletClaimTypes.CurrentEmpresaId)?.Value;
        return Guid.TryParse(raw, out empresaId);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var raw = Context.User?.FindFirst(MilletClaimTypes.Sub)?.Value;
        return Guid.TryParse(raw, out userId);
    }
}

/// <summary>
/// DTO de presencia que el hub devuelve en <c>GetPresence</c> y emite en
/// el evento <c>userPresence</c> al grupo de empresa. Forma estable para
/// que el FE no tenga que mapear shapes diferentes según el origen.
/// </summary>
public sealed record UserPresenceDto(
    Guid UserId,
    string UserNombre,
    SoftLockModo Modo,
    DateTimeOffset SinceUtc,
    DateTimeOffset LastSeenUtc);

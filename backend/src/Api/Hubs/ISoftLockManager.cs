namespace Millet.Api.Hubs;

/// <summary>
/// Manager in-memory de soft locks (CollaborationHub Sprint 2, ADR-0012
/// Capa 2). Singleton — el estado vive en el proceso del hub. Azure SignalR
/// Service ya enruta los broadcasts entre instancias del backplane, así que
/// para 30-50 usuarios concurrentes una instancia basta y los heartbeats
/// hacen converger el estado tras reconexiones.
/// </summary>
public interface ISoftLockManager
{
    /// <summary>
    /// El usuario declara presencia sobre un recurso. Si ya existía una
    /// entry para este <c>connectionId</c> (en otro recurso o con otro
    /// modo), se reemplaza — una conexión = un recurso a la vez. Tras la
    /// actualización publica <c>userPresence</c> al grupo de la empresa
    /// con la lista actual de presentes en el recurso.
    /// </summary>
    Task TrackAsync(
        Guid empresaId,
        Guid userId,
        string userNombre,
        string connectionId,
        string entidad,
        Guid entidadId,
        SoftLockModo modo,
        CancellationToken cancellationToken);

    /// <summary>
    /// Refresca el <c>LastSeenUtc</c> de la entry asociada a la conexión.
    /// No publica nada — el heartbeat es silencioso (1 push cada N segundos
    /// por usuario sería ruidoso).
    /// </summary>
    Task RefreshAsync(string connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Quita la entry asociada a la conexión (disconnect, LeaveResource o
    /// expiración por el worker). Publica <c>userPresence</c> al grupo de
    /// la empresa con la lista resultante (sin el usuario).
    /// </summary>
    Task ReleaseAsync(string connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Devuelve los presentes actuales en un recurso de una empresa.
    /// Usado por el hub method <c>GetPresence</c> (snapshot inicial al FE).
    /// </summary>
    IReadOnlyList<SoftLockEntry> GetForResource(Guid empresaId, string entidad, Guid entidadId);

    /// <summary>
    /// Snapshot de TODAS las entries activas — para el worker de expiración.
    /// </summary>
    IReadOnlyList<SoftLockEntry> Snapshot();
}

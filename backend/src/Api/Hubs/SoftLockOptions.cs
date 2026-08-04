namespace Millet.Api.Hubs;

/// <summary>
/// Opciones bindeadas desde la sección <c>SoftLock:</c> de configuración.
/// Defaults: heartbeat espera 30s entre clientes; el server tolera 90s
/// (3 heartbeats perdidos) antes de declarar muerta una entry; el sweep
/// del worker corre cada 30s.
/// </summary>
public sealed class SoftLockOptions
{
    public const string SectionName = "SoftLock";

    /// <summary>
    /// TTL de un heartbeat. Una entry con <c>LastSeenUtc &lt; now - TTL</c>
    /// se expira en el próximo sweep. Default 90s.
    /// </summary>
    public int HeartbeatExpirationSeconds { get; init; } = 90;

    /// <summary>
    /// Intervalo entre sweeps del <c>SoftLockExpirationWorker</c>. Default 30s.
    /// </summary>
    public int SweepIntervalSeconds { get; init; } = 30;
}

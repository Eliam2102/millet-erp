namespace Millet.Almacen.Infrastructure.Workers;

/// <summary>
/// Configuración del <see cref="ReordenWorker"/> (ADR-0047 PR5.D). Se bindea desde
/// la sección <c>ReordenWorker</c> (appsettings / KV).
///
/// <para>
/// <see cref="Disabled"/> arranca en <c>true</c> por diseño: mergear 5.D NO debe
/// generar RQs automáticas hasta que se habilite explícitamente por ambiente (cuando
/// 5.E/5.F estén listos para revisarlas). Opt-in con <c>ReordenWorker:Disabled=false</c>.
/// </para>
/// </summary>
public sealed class ReordenWorkerOptions
{
    public const string SectionName = "ReordenWorker";

    /// <summary>Si <c>true</c> (default), el worker no corre. Opt-in por ambiente.</summary>
    public bool Disabled { get; set; } = true;

    /// <summary>Cadencia del barrido. Default 1 hora.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Id del advisory lock PG (exclusión entre réplicas). Distinto del de
    /// IdempotencyKeysCleanupJob (7_390_001).
    /// </summary>
    public long AdvisoryLockId { get; set; } = 7_390_047L;
}

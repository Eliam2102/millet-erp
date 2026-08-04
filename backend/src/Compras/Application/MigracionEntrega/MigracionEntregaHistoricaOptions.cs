namespace Millet.Compras.Application.MigracionEntrega;

/// <summary>
/// Opciones del job one-shot de migración de históricas (ADR-0043 PR #4).
/// Se bindean desde la sección <c>Migraciones:EntregaHistorica</c>.
///
/// <para>
/// <b>Procedimiento operativo (runbook):</b>
/// <list type="number">
///   <item>Despliega con <see cref="Enabled"/>=false (default) — el job no corre.</item>
///   <item>Corre los 3 queries de diagnóstico (gate). Si hay ambiguas (1c &gt; 0),
///     revísalas antes de continuar.</item>
///   <item><see cref="Enabled"/>=true + <see cref="DryRun"/>=true → al arranque
///     <b>reporta</b> qué haría (sin escribir). Revisa el log.</item>
///   <item><see cref="DryRun"/>=false → al arranque <b>aplica</b> (log fuerte).</item>
///   <item><b>Tras la corrida real exitosa: <see cref="Enabled"/>=false</b> de
///     nuevo, para que un redeploy/reinicio no re-arme el job. (Re-correr es
///     idempotente, pero el apagado evita ruido y trabajo inútil.)</item>
/// </list>
/// </para>
/// </summary>
public sealed class MigracionEntregaHistoricaOptions
{
    public const string SectionName = "Migraciones:EntregaHistorica";

    /// <summary>
    /// Si <c>false</c> (default), el job es no-op. Se prende explícitamente en
    /// Azure para la corrida one-shot, y se vuelve a apagar tras aplicar.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Si <c>true</c> (default), el job <b>reporta</b> qué haría sin escribir
    /// nada. Se pone <c>false</c> para aplicar de verdad.
    /// </summary>
    public bool DryRun { get; set; } = true;
}

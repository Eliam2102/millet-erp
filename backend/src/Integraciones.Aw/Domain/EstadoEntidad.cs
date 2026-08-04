namespace Millet.Integraciones.Aw.Domain;

/// <summary>
/// Estados del ciclo de vida de <see cref="EntidadExterna"/>. Ver
/// <c>docs/integration/02-edi-correlation.md</c> §3.5 (diagrama de
/// transiciones). Estados finales: <see cref="Correlated"/>,
/// <see cref="ManuallyResolved"/>. Activos (siguen siendo procesados):
/// <see cref="Submitted"/>, <see cref="FailedDrop"/>,
/// <see cref="FailedCorrelation"/>.
///
/// <para>
/// <b>PR #201 — retiro de <c>AwaitingCorrelation</c>:</b> con el flow
/// per-EDI (drop service bloquea hasta tener outcome), la transición
/// <c>Submitted → AwaitingCorrelation → Correlated</c> colapsa a una
/// sola: <c>Submitted → Correlated</c> (o <c>FailedCorrelation</c> /
/// <c>FailedDrop</c> según outcome del log de A+W). El valor del enum
/// se conserva como <c>[Obsolete]</c> para no romper la deserialización
/// de filas legacy (la migración <c>RemoveAwaitingCorrelation</c> mueve
/// las que existían a <c>FailedCorrelation</c>).
/// </para>
/// </summary>
public enum EstadoEntidad : short
{
    Submitted = 0,

    [System.Obsolete(
        "PR #201 retiró este estado. El flow per-EDI transiciona directo " +
        "Submitted → Correlated/FailedCorrelation/FailedDrop. Se mantiene " +
        "el valor para preservar la deserialización de filas legacy. " +
        "Migración de remediación: 20260516xxxxxx_RemoveAwaitingCorrelation.")]
    AwaitingCorrelation = 1,

    Correlated = 2,
    FailedDrop = 3,
    FailedCorrelation = 4,
    ManuallyResolved = 5,
}

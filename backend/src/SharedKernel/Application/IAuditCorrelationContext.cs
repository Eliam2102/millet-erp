namespace Millet.SharedKernel.Application;

/// <summary>
/// Agrupa en un solo <c>CorrelationId</c> de <c>core.audit_log</c> las
/// escrituras de varios <c>SaveChanges</c> que forman una sola operación de
/// negocio. Sin esto cada <c>SaveChanges</c> genera su propia correlación y,
/// por ejemplo, en el alta de colaborador el Usuario (Identidad) y el
/// Empleado (Compartido) quedarían como operaciones distintas en la
/// bitácora (F1-ADM-01, plan 15, hallazgo F0 §4.3).
///
/// Mismo mecanismo que <see cref="IAuditOriginContext"/>
/// (<c>AsyncLocal</c> + <c>IDisposable</c>).
/// </summary>
public interface IAuditCorrelationContext
{
    /// <summary>Correlación fijada para el scope actual; null si no hay.</summary>
    Guid? CorrelationId { get; }

    /// <summary>
    /// Fija <paramref name="correlationId"/> para las escrituras dentro del
    /// scope. Disposable: usar en bloque <c>using</c>.
    /// </summary>
    IDisposable Begin(Guid correlationId);
}

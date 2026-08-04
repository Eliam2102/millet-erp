namespace Millet.SharedKernel.Domain;

/// <summary>
/// Marker que activa el registro automático en <c>core.audit_log</c>. El
/// AuditSaveChangesInterceptor escribe el snapshot (en CREATE) o el diff
/// campo a campo (en UPDATE) de las entidades marcadas en cada
/// <c>SaveChanges</c>. Cero código de auditoría por entidad.
/// Ver ADR-0008.
/// </summary>
public interface IAuditable
{
}

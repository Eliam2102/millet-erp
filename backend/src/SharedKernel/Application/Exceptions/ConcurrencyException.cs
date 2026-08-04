using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Exceptions;

/// <summary>
/// Otro proceso modificó la entidad mientras se editaba (optimistic
/// concurrency fallido). Lanzada por el BaseDbContext cuando
/// <c>BaseEntity.Version</c> no coincide. Mapea a HTTP 409 (Conflict).
/// Ver ADR-0012.
/// </summary>
public sealed class ConcurrencyException : DomainException
{
    public override string Code => "CONCURRENCY_CONFLICT";

    public string EntityType { get; }

    public Guid EntityId { get; }

    public ConcurrencyException(string entityType, Guid entityId)
        : base($"Conflicto de concurrencia: la entidad '{entityType}' con id '{entityId}' fue modificada por otra transacción.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}

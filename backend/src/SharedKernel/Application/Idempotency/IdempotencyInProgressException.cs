using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// Otro request con la misma <c>Idempotency-Key</c> está en estado
/// <c>processing</c>. El cliente debe esperar y reintentar.
/// Mapea a HTTP 409 con header <c>Retry-After: 5</c>. Ver ADR-0020.
/// </summary>
public sealed class IdempotencyInProgressException : DomainException
{
    public override string Code => "IDEMPOTENCY_IN_PROGRESS";

    /// <summary>Segundos sugeridos al cliente antes de reintentar.</summary>
    public int RetryAfterSeconds { get; }

    public IdempotencyInProgressException(int retryAfterSeconds = 5)
        : base("Otro request con la misma Idempotency-Key está en proceso. Reintente en breve.")
    {
        RetryAfterSeconds = retryAfterSeconds;
    }
}

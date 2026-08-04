using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// El endpoint requiere <c>Idempotency-Key</c> (atributo
/// <c>[RequireIdempotencyKey]</c>) pero el header está ausente.
/// Mapea a HTTP 400. Ver ADR-0020.
/// </summary>
public sealed class IdempotencyKeyMissingException : DomainException
{
    public override string Code => "MISSING_IDEMPOTENCY_KEY";

    public IdempotencyKeyMissingException()
        : base("El header 'Idempotency-Key' es obligatorio en este endpoint.")
    {
    }
}

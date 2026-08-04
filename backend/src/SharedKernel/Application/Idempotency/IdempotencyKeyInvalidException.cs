using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// El header <c>Idempotency-Key</c> está presente pero no cumple el formato
/// UUID v4. Mapea a HTTP 400. Ver ADR-0020.
/// </summary>
public sealed class IdempotencyKeyInvalidException : DomainException
{
    public override string Code => "INVALID_IDEMPOTENCY_KEY";

    public IdempotencyKeyInvalidException()
        : base("El header 'Idempotency-Key' debe ser un UUID v4 válido.")
    {
    }
}

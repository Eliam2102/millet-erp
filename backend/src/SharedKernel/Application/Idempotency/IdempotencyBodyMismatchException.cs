using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// Misma <c>Idempotency-Key</c> reusada con un body distinto al original.
/// Indica bug en el cliente HTTP: las keys deben ser estables por operación
/// lógica. Mapea a HTTP 422. Ver ADR-0020.
/// </summary>
public sealed class IdempotencyBodyMismatchException : DomainException
{
    public override string Code => "IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY";

    public IdempotencyBodyMismatchException()
        : base("La Idempotency-Key fue reusada con un body distinto al request original.")
    {
    }
}

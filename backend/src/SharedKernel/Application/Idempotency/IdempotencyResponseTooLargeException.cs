using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// El request original se completó pero su respuesta no se pudo cachear
/// (excede el límite o no es JSON). Un retry no puede devolver el mismo
/// resultado sin re-ejecutar, así que se rechaza. Mapea a HTTP 409.
/// Ver ADR-0020 §"Tamaño del body cacheable".
/// </summary>
public sealed class IdempotencyResponseTooLargeException : DomainException
{
    public override string Code => "IDEMPOTENCY_RESPONSE_TOO_LARGE";

    public IdempotencyResponseTooLargeException()
        : base("La operación ya se completó, pero su respuesta no se conservó para repetirla.")
    {
    }
}

using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Idempotency;

/// <summary>
/// El request original con esta <c>Idempotency-Key</c> terminó en error de
/// servidor (5xx) y pudo dejar efectos parciales. No se re-ejecuta con la
/// misma key: el cliente debe revisar el estado del recurso y, si decide
/// reintentar, hacerlo con una key nueva. Mapea a HTTP 409 sin
/// <c>Retry-After</c> (no es un estado transitorio). Ver ADR-0020 §"Revisión 2026-09".
/// </summary>
public sealed class IdempotencyPreviousFailureException : DomainException
{
    public override string Code => "IDEMPOTENCY_PREVIOUS_FAILURE";

    public IdempotencyPreviousFailureException()
        : base("La operación anterior con esta Idempotency-Key falló en el servidor. Revisa el estado del registro antes de reintentar.")
    {
    }
}

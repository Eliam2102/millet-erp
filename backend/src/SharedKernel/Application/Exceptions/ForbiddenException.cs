using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Exceptions;

/// <summary>
/// Usuario autenticado sin permiso para la acción solicitada. Mapea a HTTP 403.
/// Distinto de <see cref="UnauthorizedAccessException"/>, que se reserva para
/// tokens inválidos o ausentes (HTTP 401).
/// Ver ADR-0010 y ADR-0007.
/// </summary>
public sealed class ForbiddenException : DomainException
{
    public override string Code { get; }

    public ForbiddenException(string code, string message) : base(message)
    {
        Code = code;
    }
}

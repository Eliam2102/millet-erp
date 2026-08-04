using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Exceptions;

/// <summary>
/// Violación de una regla de negocio (estado inválido, transición no
/// permitida, condición no cumplida). Mapea a HTTP 422 (Unprocessable Entity).
/// Ver ADR-0010 y ADR-0018.
/// </summary>
public sealed class BusinessRuleException : DomainException
{
    public override string Code { get; }

    public BusinessRuleException(string code, string message) : base(message)
    {
        Code = code;
    }

    public BusinessRuleException(string code, string message, Exception inner)
        : base(message, inner)
    {
        Code = code;
    }
}

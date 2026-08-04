using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Exceptions;

/// <summary>
/// Recurso referenciado no existe (por id, por código, etc.). Mapea a HTTP 404.
/// Ver ADR-0010 y ADR-0018.
/// </summary>
public sealed class EntityNotFoundException : DomainException
{
    public override string Code { get; }

    public EntityNotFoundException(string code, string message) : base(message)
    {
        Code = code;
    }
}

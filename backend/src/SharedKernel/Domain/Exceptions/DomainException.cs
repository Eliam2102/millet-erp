namespace Millet.SharedKernel.Domain.Exceptions;

/// <summary>
/// Base para todas las excepciones de dominio del sistema. Cada subtipo
/// expone un <see cref="Code"/> identificador estable para que el frontend
/// pueda branchar lógicamente sin parsear el mensaje. El middleware de
/// Problem Details (ADR-0010) mapea cada subtipo a un código HTTP específico.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>
    /// Código identificador estable de este tipo de error. Convención:
    /// <c>SCREAMING_SNAKE_CASE</c>. Ver ADR-0018.
    /// </summary>
    public abstract string Code { get; }

    protected DomainException(string message) : base(message) { }

    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

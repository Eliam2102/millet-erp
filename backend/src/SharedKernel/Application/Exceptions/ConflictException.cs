using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Exceptions;

/// <summary>
/// Conflicto con el estado actual del recurso: violación de unicidad de
/// business key (RFC duplicado, clave duplicada, etc.) o transición de
/// estado no permitida. Mapea a HTTP 409 (Conflict). Ver ADR-0010 y ADR-0018.
///
/// <para>
/// Distinto de <see cref="BusinessRuleException"/> (HTTP 422): éste se usa
/// para conflictos de identidad/unicidad o transiciones inválidas donde
/// otro intento con datos/estado distintos sí podría tener éxito; el 422
/// cubre violaciones de reglas semánticas. Convención introducida en
/// F-Admin-PR2.3.
/// </para>
///
/// <para>
/// PR D (Integraciones.Aw): cambio de <c>sealed</c> a abierta para herencia
/// para que excepciones específicas de dominio (ej.
/// <c>QuoteReferenceDuplicadaException</c>, <c>InvalidStateTransitionException</c>)
/// puedan heredarla y mapear automáticamente a 409 vía
/// <see cref="GlobalExceptionHandler"/>. Sin breaking change retro: las
/// instancias directas de <c>ConflictException</c> siguen funcionando
/// idéntico.
/// </para>
/// </summary>
public class ConflictException : DomainException
{
    public override string Code { get; }

    public ConflictException(string code, string message) : base(message)
    {
        Code = code;
    }

    public ConflictException(string code, string message, Exception inner)
        : base(message, inner)
    {
        Code = code;
    }
}

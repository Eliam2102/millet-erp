using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Domain.Exceptions;

/// <summary>
/// Se lanza cuando un método de dominio intenta cambiar el estado de
/// una entidad desde un estado que no permite esa transición (ver
/// diagrama §3.5 de <c>02-edi-correlation.md</c>).
///
/// <para>
/// Type Problem Details: <c>https://millet-erp/errors/aw_invalid_state_transition</c>.
/// Se mapea a HTTP 409 Conflict (heredado del mapping de
/// <see cref="ConflictException"/> en <c>GlobalExceptionHandler</c> — PR D)
/// porque el cliente intentó una operación válida sobre un recurso en
/// estado incompatible (no es un error de input — el input es válido
/// pero la entidad ya está en estado final, ej. correlated).
/// </para>
/// </summary>
public sealed class InvalidStateTransitionException : ConflictException
{
    public const string CodeValue = "AW_INVALID_STATE_TRANSITION";

    public EstadoEntidad EstadoActual { get; }
    public string TransicionIntentada { get; }

    public InvalidStateTransitionException(EstadoEntidad estadoActual, string transicionIntentada)
        : base(
            CodeValue,
            $"No se puede ejecutar '{transicionIntentada}' desde el estado '{estadoActual}'. " +
            "Ver docs/integration/02-edi-correlation.md §3.5 para el diagrama de transiciones permitidas.")
    {
        EstadoActual = estadoActual;
        TransicionIntentada = transicionIntentada;
    }
}

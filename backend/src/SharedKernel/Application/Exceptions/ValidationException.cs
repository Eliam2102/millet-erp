using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Application.Exceptions;

/// <summary>
/// Errores de validación estructural del input (longitudes, formatos,
/// campos requeridos). Lanzada por el middleware de validación cuando
/// FluentValidation reporta fallas. Mapea a HTTP 400 (Bad Request).
/// Ver ADR-0010 y ADR-0018.
/// </summary>
public sealed class ValidationException : DomainException
{
    public override string Code => "VALIDATION_ERROR";

    public IReadOnlyList<ValidationError> Errors { get; }

    public ValidationException(IEnumerable<ValidationError> errors)
        : this(errors.ToList())
    {
    }

    // El mensaje nombra campo y motivo: se persiste tal cual en bandejas/solicitudes (ej. ingesta A+W).
    private ValidationException(List<ValidationError> errors)
        : base("Errores de validación: " + string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}")))
    {
        Errors = errors;
    }
}

/// <summary>
/// Error individual dentro de un <see cref="ValidationException"/>. El
/// frontend mapea <see cref="Field"/> al input correspondiente del formulario
/// para mostrar el <see cref="Message"/> inline.
/// </summary>
public sealed record ValidationError(string Field, string Code, string Message);

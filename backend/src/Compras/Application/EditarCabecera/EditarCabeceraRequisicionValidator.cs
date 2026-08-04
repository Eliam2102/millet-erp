using FluentValidation;

namespace Millet.Compras.Application.EditarCabecera;

public sealed class EditarCabeceraRequisicionValidator
    : AbstractValidator<EditarCabeceraRequisicionCommand>
{
    public EditarCabeceraRequisicionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEqual(Guid.Empty);
        // Descripcion: si viene un valor, validar longitud.
        RuleFor(c => c.Descripcion!)
            .MaximumLength(500)
            .When(c => c.Descripcion is not null);
        RuleFor(c => c.Prioridad)
            .IsInEnum()
            .When(c => c.Prioridad is not null);
        RuleFor(c => c.Clasificacion)
            .IsInEnum()
            .When(c => c.Clasificacion is not null);
    }
}

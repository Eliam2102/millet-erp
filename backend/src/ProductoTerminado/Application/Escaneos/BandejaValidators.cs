using FluentValidation;

namespace Millet.ProductoTerminado.Application.Escaneos;

public sealed class DescartarEscaneoValidator : AbstractValidator<DescartarEscaneoCommand>
{
    public DescartarEscaneoValidator()
    {
        RuleFor(x => x.Motivo).Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("El motivo es obligatorio.");
        RuleFor(x => x.Motivo).MaximumLength(500);
    }
}

using FluentValidation;

namespace Millet.ProductoTerminado.Application.Escaneos;

public sealed class RegistrarEscaneoPiezaValidator : AbstractValidator<RegistrarEscaneoPiezaCommand>
{
    public RegistrarEscaneoPiezaValidator()
    {
        RuleFor(x => x.EmpresaId).NotEmpty();
        RuleFor(x => x.Etiqueta).NotEmpty().MaximumLength(100);
        RuleFor(x => x.NumeroPedido).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Origen).Must(x => x is "Demo" or "Real").WithMessage("El origen debe ser Demo o Real.");
    }
}

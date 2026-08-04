using FluentValidation;

namespace Millet.Compras.Application.Aprobadores;

public sealed class DesignarAprobadorValidator : AbstractValidator<DesignarAprobadorCommand>
{
    public DesignarAprobadorValidator()
    {
        RuleFor(c => c.DepartamentoId).NotEqual(Guid.Empty);
        RuleFor(c => c.UsuarioId).NotEqual(Guid.Empty);
        RuleFor(c => c.Rol).IsInEnum();
        RuleFor(c => c.Motivo).MaximumLength(500);
    }
}

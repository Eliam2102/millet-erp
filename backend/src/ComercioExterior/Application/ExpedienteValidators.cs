using FluentValidation;

namespace Millet.ComercioExterior.Application;
public sealed class CrearExpedienteValidator : AbstractValidator<CrearExpedienteCommand>
{
    public CrearExpedienteValidator()
    {
        RuleFor(x => x.Tipo).IsInEnum();
        RuleFor(x => x.Referencia).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ResponsableUsuarioId).NotEmpty();
        RuleFor(x => x.Clientes).Must(x => x is null || x.Length <= 100).WithMessage("La solicitud admite hasta 100 clientes.");
        RuleForEach(x => x.Clientes).NotEmpty().MaximumLength(200);
    }
}

public sealed class MutarExpedienteValidator : AbstractValidator<MutarExpedienteCommand>
{
    public MutarExpedienteValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Accion).Must(x => x is "editar" or "participantes" or "documentos" or "revisar-documentos" or "cancelar");
    }
}

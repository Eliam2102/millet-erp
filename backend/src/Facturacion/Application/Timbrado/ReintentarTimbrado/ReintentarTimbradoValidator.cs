using FluentValidation;

namespace Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;

public sealed class ReintentarTimbradoValidator : AbstractValidator<ReintentarTimbradoCommand>
{
    public ReintentarTimbradoValidator()
    {
        RuleFor(c => c.ComprobanteId).NotEqual(Guid.Empty);
    }
}

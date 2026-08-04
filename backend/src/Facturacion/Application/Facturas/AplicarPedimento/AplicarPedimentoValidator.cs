using FluentValidation;

namespace Millet.Facturacion.Application.Facturas.AplicarPedimento;

/// <summary>Validación estructural (FluentValidation).</summary>
public sealed class AplicarPedimentoValidator : AbstractValidator<AplicarPedimentoCommand>
{
    public AplicarPedimentoValidator()
    {
        RuleFor(c => c.FacturaVentaId).NotEmpty();
        RuleFor(c => c.Pedimento).NotEmpty().MaximumLength(21);
        RuleFor(c => c.IdentificacionMercancia).MaximumLength(50);
    }
}

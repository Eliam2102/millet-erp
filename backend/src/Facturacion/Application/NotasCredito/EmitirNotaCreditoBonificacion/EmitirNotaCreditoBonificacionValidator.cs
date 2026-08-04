using FluentValidation;

namespace Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;

/// <summary>Validación estructural (FluentValidation); el negocio vive en el handler/dominio.</summary>
public sealed class EmitirNotaCreditoBonificacionValidator : AbstractValidator<EmitirNotaCreditoBonificacionCommand>
{
    public EmitirNotaCreditoBonificacionValidator()
    {
        RuleFor(c => c.FacturaVentaId).NotEmpty();
        RuleFor(c => c.MontoTotal).GreaterThan(0);
        RuleFor(c => c.TasaIva).GreaterThanOrEqualTo(0).When(c => c.TasaIva.HasValue);
    }
}

using FluentValidation;

namespace Millet.Facturacion.Application.Anticipos.VincularAnticipo;

/// <summary>Validación estructural del comando de vinculación (FluentValidation).</summary>
public sealed class VincularAnticipoValidator : AbstractValidator<VincularAnticipoCommand>
{
    public VincularAnticipoValidator()
    {
        RuleFor(c => c.AnticipoId).NotEmpty();
        RuleFor(c => c.FacturaVentaId).NotEmpty();
        RuleFor(c => c.Importe).GreaterThan(0);
    }
}

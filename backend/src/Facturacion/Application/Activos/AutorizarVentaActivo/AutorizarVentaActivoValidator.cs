using FluentValidation;

namespace Millet.Facturacion.Application.Activos.AutorizarVentaActivo;

/// <summary>Validación estructural (FluentValidation).</summary>
public sealed class AutorizarVentaActivoValidator : AbstractValidator<AutorizarVentaActivoCommand>
{
    public AutorizarVentaActivoValidator()
    {
        RuleFor(c => c.ActivoRef).NotEmpty().MaximumLength(50);
        RuleFor(c => c.PrecioVenta).GreaterThan(0);
    }
}

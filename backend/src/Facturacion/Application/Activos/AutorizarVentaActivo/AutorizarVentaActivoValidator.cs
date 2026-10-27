using FluentValidation;

namespace Millet.Facturacion.Application.Activos.AutorizarVentaActivo;

/// <summary>Validación estructural (FluentValidation).</summary>
public sealed class AutorizarVentaActivoValidator : AbstractValidator<AutorizarVentaActivoCommand>
{
    public AutorizarVentaActivoValidator()
    {
        RuleFor(c => c.ActivoRef).NotEmpty().MaximumLength(60);
        RuleFor(c => c.PrecioVenta).GreaterThan(0);
        RuleFor(c => c.Motivo).IsInEnum().NotEmpty();
        RuleFor(c => c.SolicitadoPor).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Evidencia).NotEmpty();
    }
}

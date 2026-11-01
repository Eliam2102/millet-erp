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
        RuleFor(c => c.Reembolso!).SetValidator(new ReembolsoClienteRequestValidator()).When(c => c.Reembolso is not null);
    }
}

/// <summary>F6.3: forma de la cuenta destino; el dígito verificador de la CLABE lo valida el dominio.</summary>
public sealed class ReembolsoClienteRequestValidator : AbstractValidator<ReembolsoClienteRequest>
{
    public ReembolsoClienteRequestValidator()
    {
        RuleFor(r => r.Banco).NotEmpty().MaximumLength(80);
        RuleFor(r => r.Clabe).NotEmpty().Matches("^[0-9]{18}$").WithMessage("La CLABE debe tener 18 dígitos.");
        RuleFor(r => r.Titular).NotEmpty().MaximumLength(200);
    }
}

using FluentValidation;

namespace Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;

/// <summary>Validación estructural (FluentValidation); el negocio vive en el handler/dominio.</summary>
public sealed class EmitirCartaPorteValidator : AbstractValidator<EmitirCartaPorteCommand>
{
    public EmitirCartaPorteValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.TipoCfdi).NotEmpty().Must(t => t is "T" or "I").WithMessage("El tipo de Carta Porte debe ser T o I.");
        RuleFor(c => c.ReceptorRfc).NotEmpty().MaximumLength(13);
        RuleFor(c => c.RfcEmisor).NotEmpty().MaximumLength(13);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        RuleFor(c => c.Origen).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Destino).NotEmpty().MaximumLength(200);
        RuleFor(c => c.VehiculoId).NotEmpty();
        RuleFor(c => c.OperadorId).NotEmpty();
        RuleFor(c => c.Mercancias).NotEmpty().WithMessage("La Carta Porte debe llevar al menos una mercancía.");
        RuleFor(c => c.MontoServicio).GreaterThan(0).When(c => c.TipoCfdi == "I")
            .WithMessage("Una Carta Porte tipo I debe facturar el servicio (monto > 0).");
    }
}

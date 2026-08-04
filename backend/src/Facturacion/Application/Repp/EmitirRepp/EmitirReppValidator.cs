using FluentValidation;

namespace Millet.Facturacion.Application.Repp.EmitirRepp;

/// <summary>Validación estructural (FluentValidation); el negocio (PPD, saldos) vive en el handler/dominio.</summary>
public sealed class EmitirReppValidator : AbstractValidator<EmitirReppCommand>
{
    public EmitirReppValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.CanalVentaId).GreaterThan((short)0).When(c => c.CanalVentaId.HasValue);
        RuleFor(c => c.MonedaPago).NotEmpty().Length(3);
        RuleFor(c => c.FormaPagoReal).NotEmpty().MaximumLength(5);
        RuleFor(c => c.Facturas).NotEmpty().WithMessage("El REPP debe cubrir al menos una factura.");
        RuleForEach(c => c.Facturas).ChildRules(f =>
        {
            f.RuleFor(x => x.FacturaVentaId).NotEmpty();
            f.RuleFor(x => x.ImportePagado).GreaterThan(0);
        });
    }
}

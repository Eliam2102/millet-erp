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
        // 86e3tfdve: longitud máxima del SAT (patrón [A-Z0-9_]{10,50}); el formato
        // por forma de pago (c_FormaPago) lo valida CuentasPagoSat en el handler.
        RuleFor(c => c.CuentaOrdenante).MaximumLength(50);
        RuleFor(c => c.CuentaBeneficiaria).MaximumLength(50);
        RuleFor(c => c.CuentaBancariaBeneficiariaId).NotEqual(Guid.Empty).When(c => c.CuentaBancariaBeneficiariaId.HasValue);
        RuleFor(c => c.Facturas).NotEmpty().WithMessage("El REPP debe cubrir al menos una factura.");
        RuleForEach(c => c.Facturas).ChildRules(f =>
        {
            f.RuleFor(x => x.FacturaVentaId).NotEmpty();
            f.RuleFor(x => x.ImportePagado).GreaterThan(0);
        });
    }
}

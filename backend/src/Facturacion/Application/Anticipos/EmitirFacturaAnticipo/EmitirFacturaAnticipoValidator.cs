using FluentValidation;

namespace Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;

/// <summary>
/// Validación estructural del comando de emisión de anticipo (FluentValidation).
/// Las reglas de negocio (período, catálogos SAT, receptor nominal) viven en el
/// handler/dominio.
/// </summary>
public sealed class EmitirFacturaAnticipoValidator : AbstractValidator<EmitirFacturaAnticipoCommand>
{
    public EmitirFacturaAnticipoValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.ClienteId).NotEmpty();

        RuleFor(c => c.ReceptorRfc).NotEmpty().MaximumLength(13);
        RuleFor(c => c.ReceptorNombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.ReceptorRegimenFiscal).NotEmpty().MaximumLength(5);
        RuleFor(c => c.ReceptorCodigoPostal).NotEmpty().MaximumLength(10);
        RuleFor(c => c.ReceptorUsoCfdi).NotEmpty().MaximumLength(5);
        RuleFor(c => c.ReceptorPais).NotEmpty().MaximumLength(5);

        RuleFor(c => c.RfcEmisor).NotEmpty().MaximumLength(13);
        RuleFor(c => c.RegimenFiscalEmisor).NotEmpty().MaximumLength(5);

        RuleFor(c => c.MetodoPago).NotEmpty().MaximumLength(5);
        RuleFor(c => c.FormaPago).NotEmpty().MaximumLength(5);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);

        // Matriz SAT método/forma de pago (misma regla que factura de venta;
        // falla ANTES de quemar folio — CFDI40105).
        RuleFor(c => c.FormaPago)
            .Equal("99")
            .When(c => c.MetodoPago == "PPD")
            .WithMessage("Con método de pago PPD (pago en parcialidades o diferido) la forma de pago debe ser 99 — Por definir (regla SAT CFDI40105).");
        RuleFor(c => c.FormaPago)
            .NotEqual("99")
            .When(c => c.MetodoPago == "PUE")
            .WithMessage("Con método de pago PUE (pago en una sola exhibición) captura la forma de pago real del cobro — 99 (Por definir) solo aplica a PPD.");

        RuleFor(c => c.TipoAnticipo).IsInEnum();
        RuleFor(c => c.MontoBase).GreaterThan(0);
        RuleFor(c => c.TasaIvaTraslado).GreaterThanOrEqualTo(0).When(c => c.TasaIvaTraslado.HasValue);
        RuleFor(c => c.CanalVentaId).GreaterThan((short)0).When(c => c.CanalVentaId.HasValue);
    }
}

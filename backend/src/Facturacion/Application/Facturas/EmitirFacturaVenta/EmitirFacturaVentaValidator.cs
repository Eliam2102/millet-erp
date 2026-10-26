using FluentValidation;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;

/// <summary>
/// Validación estructural del comando de emisión (FluentValidation, no Data
/// Annotations). Las validaciones de catálogo SAT y de negocio (período, folio)
/// viven en el handler/dominio; aquí solo la forma del request.
/// </summary>
public sealed class EmitirFacturaVentaValidator : AbstractValidator<EmitirFacturaVentaCommand>
{
    public EmitirFacturaVentaValidator(ICanalesVentaReadPort canalesVenta)
    {
        RuleFor(c => c.OrdenCompraCliente).MaximumLength(60);
        RuleFor(c => c.ReferenciaCliente).MaximumLength(60);
        RuleFor(c => c.CondicionesPago).MaximumLength(60);
        RuleFor(c => c.Proforma).MaximumLength(40);
        RuleFor(c => c.SucursalId).NotEmpty();


        RuleFor(c => c.RfcEmisor).NotEmpty().MaximumLength(13);
        RuleFor(c => c.RegimenFiscalEmisor).NotEmpty().MaximumLength(5);

        RuleFor(c => c.MetodoPago).NotEmpty().MaximumLength(5);
        RuleFor(c => c.FormaPago).NotEmpty().MaximumLength(5);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);

        // Matriz SAT método/forma de pago — validada aquí para fallar ANTES
        // de quemar folio (incidente 2026-07-11: CFDI40105 en COTT-2026-3).
        RuleFor(c => c.FormaPago)
            .Equal("99")
            .When(c => c.MetodoPago == "PPD")
            .WithMessage("Con método de pago PPD (pago en parcialidades o diferido) la forma de pago debe ser 99 — Por definir (regla SAT CFDI40105).");
        RuleFor(c => c.FormaPago)
            .NotEqual("99")
            .When(c => c.MetodoPago == "PUE")
            .WithMessage("Con método de pago PUE (pago en una sola exhibición) captura la forma de pago real del cobro — 99 (Por definir) solo aplica a PPD.");

        // FAC-ING-PR2: el canal vive en el catálogo compartido.canales_venta
        // (antes enum + IsInEnum) — debe existir y estar activo.
        RuleFor(c => c.CanalVenta)
            .MustAsync(canalesVenta.ExisteActivoAsync)
            .WithMessage("El canal de venta no existe o está inactivo.");
        RuleFor(c => c.ComportamientoFiscal).IsInEnum();

        // 86e3tfdw2: observaciones del PDF (no van al CFDI).
        RuleFor(c => c.Observaciones)
            .MaximumLength(FacturaVenta.ObservacionesMaxLength)
            .WithMessage($"Las observaciones no pueden exceder {FacturaVenta.ObservacionesMaxLength} caracteres.");

        RuleFor(c => c.Lineas).NotEmpty().WithMessage("La factura debe tener al menos una línea.");
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.ClaveProdServSat).MaximumLength(10); // Faltantes se reportan por línea con 422 antes de folio/PAC.
            l.RuleFor(x => x.Descripcion).NotEmpty().MaximumLength(1000);
            l.RuleFor(x => x.ClaveUnidadSat).MaximumLength(10);
            l.RuleFor(x => x.Cantidad).GreaterThan(0);
            l.RuleFor(x => x.ValorUnitario).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.Descuento).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.Referencia).MaximumLength(80);
            l.RuleFor(x => x.PesoKg).InclusiveBetween(0m, 999999999999999.999m);
            l.RuleFor(x => x.AreaPie2).InclusiveBetween(0m, 999999999999999.999m);
        });
    }
}

using FluentValidation;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Pedidos.EditarPedidoFacturableManual;

public sealed class EditarPedidoFacturableManualValidator : AbstractValidator<EditarPedidoFacturableManualCommand>
{
    public EditarPedidoFacturableManualValidator(ICanalesVentaReadPort canalesVenta)
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.ClienteNombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        // FAC-ING-PR2: canal contra el catálogo (existencia + activo).
        RuleFor(c => c.CanalVenta)
            .MustAsync(canalesVenta.ExisteActivoAsync)
            .WithMessage("El canal de venta no existe o está inactivo.");
        RuleFor(c => c.ComportamientoFiscal).IsInEnum();
        RuleFor(c => c.Comentarios).MaximumLength(1000);

        RuleFor(c => c.Lineas).NotEmpty().WithMessage("El pedido debe tener al menos una línea.");
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductoDescripcion).NotEmpty().MaximumLength(1000);
            l.RuleFor(x => x.ClaveProdServSat).MaximumLength(10);
            l.RuleFor(x => x.ClaveUnidadSat).MaximumLength(10);
            l.RuleFor(x => x.Cantidad).GreaterThan(0);
            l.RuleFor(x => x.Precio).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.Descuento).GreaterThanOrEqualTo(0);
        });
    }
}

using FluentValidation;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;

public sealed class CrearPedidoFacturableManualValidator : AbstractValidator<CrearPedidoFacturableManualCommand>
{
    public CrearPedidoFacturableManualValidator(ICanalesVentaReadPort canalesVenta)
    {
        RuleFor(c => c.SucursalId).NotEmpty();
        RuleFor(c => c.ClienteId).NotEmpty();
        RuleFor(c => c.ClienteNombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Moneda).NotEmpty().Length(3);
        // FAC-ING-PR2: el canal vive en el catálogo compartido.canales_venta
        // (antes enum + IsInEnum) — debe existir y estar activo.
        RuleFor(c => c.CanalVenta)
            .MustAsync(canalesVenta.ExisteActivoAsync)
            .WithMessage("El canal de venta no existe o está inactivo.");
        RuleFor(c => c.ComportamientoFiscal).IsInEnum();
        RuleFor(c => c.NumeroPedido).MaximumLength(50);
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

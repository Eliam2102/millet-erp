using FluentValidation;
using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Application.Pedidos.RegistrarSalidaFacturable;

public sealed class RegistrarSalidaFacturableValidator : AbstractValidator<RegistrarSalidaFacturableCommand>
{
    public RegistrarSalidaFacturableValidator()
    {
        RuleFor(c => c).Must(c => c.PedidoFacturableId is not null || !string.IsNullOrWhiteSpace(c.NumeroPedidoAw))
            .WithName("pedidoFacturableId").WithMessage("Indica el pedido (id o número de pedido A+W).");
        RuleFor(c => c.Origen).IsInEnum();
        RuleFor(c => c.Referencia).NotEmpty().MaximumLength(SalidaFacturable.ReferenciaMaxLength);
        RuleFor(c => c.Lineas).NotEmpty().WithMessage("La salida debe tener al menos una línea.");
        RuleForEach(c => c.Lineas).ChildRules(l =>
        {
            l.RuleFor(x => x.Posicion).GreaterThan(0);
            l.RuleFor(x => x.Cantidad).GreaterThan(0);
        });
    }
}

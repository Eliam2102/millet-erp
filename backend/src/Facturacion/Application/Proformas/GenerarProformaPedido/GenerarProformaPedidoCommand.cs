using FluentValidation;
using MediatR;

namespace Millet.Facturacion.Application.Proformas.GenerarProformaPedido;

/// <summary>
/// Genera una nueva versión de la proforma de un pedido facturable (U1.8a):
/// mismo armado que el timbrado, sin reservar folio fiscal ni crear factura.
/// </summary>
public sealed record GenerarProformaPedidoCommand(Guid PedidoFacturableId) : IRequest<ProformaPedidoResponse>;

public sealed class GenerarProformaPedidoValidator : AbstractValidator<GenerarProformaPedidoCommand>
{
    public GenerarProformaPedidoValidator()
    {
        RuleFor(c => c.PedidoFacturableId).NotEmpty();
    }
}

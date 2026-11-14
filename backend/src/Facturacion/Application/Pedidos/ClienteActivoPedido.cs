using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Pedidos;

/// <summary>
/// N-14: un pedido manual nuevo no admite un cliente inactivo, igual que la factura (<c>ValidadorReceptorFiscal</c>, venta
/// nueva). El saldo y los documentos anteriores del cliente se conservan; solo se rechaza la venta nueva.
/// </summary>
public static class ClienteActivoPedido
{
    public const string Codigo = "CLIENTE_INACTIVO";

    public const string Mensaje =
        "El cliente está inactivo y no admite ventas nuevas. Su saldo y documentos anteriores se conservan.";

    /// <summary>Rechaza si el cliente existe en el maestro y está inactivo (sin puerto o sin ficha no valida: ya lo hace la emisión).</summary>
    public static async Task VerificarAsync(IClientesReadPort? clientes, Guid clienteId, CancellationToken cancellationToken)
    {
        if (clientes is null)
            return;
        var maestro = await clientes.ObtenerAsync(clienteId, cancellationToken);
        if (maestro is { Activo: false })
            throw new BusinessRuleException(Codigo, Mensaje);
    }
}

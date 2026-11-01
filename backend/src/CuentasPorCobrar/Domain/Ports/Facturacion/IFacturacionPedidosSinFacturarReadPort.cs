namespace Millet.CuentasPorCobrar.Domain.Ports.Facturacion;

/// <summary>
/// Pedido a crédito en firme sin factura vigente, con el saldo de anticipo
/// que lo cubre (C7.1, 86e3kdxx4). <c>Total</c> (con IVA) y
/// <c>AnticipoLigado</c> en la moneda del pedido.
/// </summary>
public sealed record PedidoSinFacturarDto(
    Guid PedidoId,
    string? NumeroPedido,
    string Moneda,
    decimal Total,
    decimal AnticipoLigado);

/// <summary>
/// Puerto de lectura de los pedidos que comprometen crédito sin estar
/// facturados (matriz I7 / R1: disponible = límite − facturado − liberado
/// sin factura). El adapter vive en Facturación
/// (<c>Infrastructure/PublicAdapters</c>); CxC nunca lee el esquema
/// <c>facturacion</c>. Supuesto a validar con Crédito (V22, P7.1): un
/// pedido compromete crédito desde estatus A+W 40, o desde 15 si no tiene
/// anticipo que lo cubra.
/// </summary>
public interface IFacturacionPedidosSinFacturarReadPort
{
    /// <param name="empresaId">Empresa de la línea de crédito: explícita para que funcione
    /// igual desde un request que desde un worker (decisión automática de PT5.2).</param>
    Task<IReadOnlyList<PedidoSinFacturarDto>> ListarPorClienteAsync(
        Guid empresaId, Guid clienteId, CancellationToken cancellationToken);
}

using MediatR;
using Millet.Facturacion.Domain.Facturas;

namespace Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;

/// <summary>
/// Captura un <c>PedidoFacturable</c> con <c>origen = Manual</c> + líneas inline
/// (§7.10 levantamiento, estilo Requisiciones). La validación contra los masters
/// de Cliente/Producto se hará cuando DatosMaestros sea real (F3); en F1-PR2 el
/// operador aporta el snapshot de cliente/producto.
/// </summary>
public sealed record CrearPedidoFacturableManualCommand(
    string? NumeroPedido,
    Guid SucursalId,
    Guid ClienteId,
    string ClienteNombre,
    short CanalVenta,
    ComportamientoFiscal ComportamientoFiscal,
    string Moneda,
    long? ObraId,
    string? ObraNombre,
    string? Comentarios,
    IReadOnlyList<PedidoFacturableLineaInput> Lineas) : IRequest<PedidoFacturableResponse>;

/// <summary>Línea de captura (compartida por Crear y Editar).</summary>
public sealed record PedidoFacturableLineaInput(
    Guid? ProductoId,
    string ProductoDescripcion,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    decimal Cantidad,
    decimal Precio,
    decimal Descuento,
    bool RequierePedimento);

public sealed record PedidoFacturableResponse(
    Guid Id,
    string Estado,
    decimal Total,
    int Version);

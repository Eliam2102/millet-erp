using MediatR;
using Millet.Facturacion.Application.Pedidos.CrearPedidoFacturableManual;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;

namespace Millet.Facturacion.Application.Pedidos.EditarPedidoFacturableManual;

/// <summary>
/// Edita el encabezado + líneas de un pedido manual aún no facturado.
/// <c>VersionEsperada</c> viene del header <c>If-Match</c> (ETag, ADR-0012);
/// si no coincide con la versión actual → conflicto de concurrencia (409). Sólo
/// pedidos en <c>Importado</c> son editables.
///
/// <para>F6.1: la clase es obligatoria también al editar (un pedido manual
/// legacy queda clasificado); si la clase resultante es Emergencia se exige
/// <c>facturacion.pedidos.capturar-emergencia</c>.</para>
/// </summary>
public sealed record EditarPedidoFacturableManualCommand(
    Guid Id,
    int VersionEsperada,
    Guid ClienteId,
    string ClienteNombre,
    short CanalVenta,
    ComportamientoFiscal ComportamientoFiscal,
    string Moneda,
    long? ObraId,
    string? ObraNombre,
    string? Comentarios,
    IReadOnlyList<PedidoFacturableLineaInput> Lineas,
    ClasePedidoManual? ClaseManual = null,
    SubtipoVentaDirecta? SubtipoVentaDirecta = null,
    string? MotivoEmergencia = null,
    string? ReferenciaPedidoAw = null) : IRequest<PedidoFacturableResponse>, IClasificacionPedidoManual;

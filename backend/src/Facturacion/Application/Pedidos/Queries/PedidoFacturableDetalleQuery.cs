using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Pedidos.Queries;

/// <summary>
/// Detalle de un <c>PedidoFacturable</c> con líneas y versión (B1, FE-F1): para
/// la pantalla de detalle/edición y el prefill al facturar desde pedido. La
/// <see cref="PedidoFacturableDetalleResponse.Version"/> es el ETag de
/// concurrencia (If-Match al editar, ADR-0012).
///
/// <para>
/// FAC-UX-PR1: el detalle enriquece el snapshot del pedido con los datos
/// fiscales vivos de los masters (<c>compartido.clientes</c> y
/// <c>compartido.producto_aw</c>) para que el frontend prellene la emisión sin
/// capturas manuales: <see cref="PedidoFacturableDetalleResponse.ClienteFiscal"/>
/// y las claves SAT/tasas por línea. Los masters se consultan vía puertos de
/// solo lectura — la línea persiste su snapshot y NO se muta aquí.
/// </para>
/// </summary>
public sealed record PedidoFacturableDetalleQuery(Guid Id) : IRequest<PedidoFacturableDetalleResponse>;

public sealed record PedidoFacturableDetalleResponse(
    Guid Id,
    string? NumeroPedido,
    string Origen,
    string Estado,
    Guid SucursalId,
    Guid ClienteId,
    string ClienteNombre,
    // FAC-ING-PR2: id + nombre del catálogo compartido.canales_venta. El id
    // prellena el selector al editar; el nombre es para display (histórico
    // incluido — un canal desactivado sigue mostrando su nombre).
    short CanalVentaId,
    string CanalVenta,
    string ComportamientoFiscal,
    string Moneda,
    long? ObraId,
    string? ObraNombre,
    string? Comentarios,
    Guid? ComprobanteVigenteId,
    decimal Total,
    // RANURA-PR1: descuento de cabecera A+W (bruto, con IVA). No se resta en
    // el CFDI — se documenta con NC (relación 01) y la caja cobra total − NC.
    decimal? Ranura,
    int Version,
    PedidoClienteFiscal? ClienteFiscal,
    IReadOnlyList<PedidoFacturableLineaDetalle> Lineas);

/// <summary>
/// Datos fiscales vivos del cliente en el master (<c>compartido.clientes</c>).
/// <c>null</c> total si el cliente no existe en el master; campos individuales
/// en <c>null</c> = incompletos (gap G12: régimen/CP no vienen de A+W y se
/// completan en /admin/datos-maestros/clientes) — no bloquean el detalle,
/// bloquean el timbrado.
/// </summary>
public sealed record PedidoClienteFiscal(
    string? Rfc,
    string? RegimenFiscal,
    string? CodigoPostalFiscal,
    string? UsoCfdiDefault,
    string? FormaPagoDefault,
    string? MetodoPagoDefault,
    bool EsGenerico);

public sealed record PedidoFacturableLineaDetalle(
    int Posicion,
    Guid? ProductoId,
    string ProductoDescripcion,
    string? ClaveProdServSat,
    string? ClaveUnidadSat,
    decimal Cantidad,
    decimal Precio,
    decimal Descuento,
    bool RequierePedimento,
    string? ObjetoImp,
    decimal? TasaIvaTraslado,
    decimal? TasaRetencionIva,
    decimal? TasaRetencionIsr);

public sealed class PedidoFacturableDetalleHandler
    : IRequestHandler<PedidoFacturableDetalleQuery, PedidoFacturableDetalleResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IClientesReadPort _clientes;
    private readonly IProductosReadPort _productos;
    private readonly ICanalesVentaReadPort _canalesVenta;
    private readonly IAlcanceCajaEvaluator _alcance;

    public PedidoFacturableDetalleHandler(
        FacturacionDbContext db,
        IClientesReadPort clientes,
        IProductosReadPort productos,
        ICanalesVentaReadPort canalesVenta,
        IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _clientes = clientes;
        _productos = productos;
        _canalesVenta = canalesVenta;
        _alcance = alcance;
    }

    public async Task<PedidoFacturableDetalleResponse> Handle(
        PedidoFacturableDetalleQuery query, CancellationToken cancellationToken)
    {
        // Fuera de alcance → mismo 404 que inexistente (12-cajas.md §4.1).
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var pedido = await alcance.AplicarA(_db.PedidosFacturables.AsNoTracking())
            .Include(p => p.Lineas)
            .FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO", $"No existe el pedido facturable '{query.Id}'.");

        var clienteMaster = await _clientes.ObtenerAsync(pedido.ClienteId, cancellationToken);
        var clienteFiscal = clienteMaster is null
            ? null
            : new PedidoClienteFiscal(
                clienteMaster.Rfc, clienteMaster.RegimenFiscal, clienteMaster.CodigoPostalFiscal,
                clienteMaster.UsoCfdiDefault, clienteMaster.FormaPagoDefault,
                clienteMaster.MetodoPagoDefault, clienteMaster.EsGenerico);

        // Un lookup por producto distinto; el mismo producto puede repetirse
        // en varias posiciones del pedido.
        var productosMaster = new Dictionary<Guid, ProductoFiscalLectura?>();
        foreach (var productoId in pedido.Lineas
                     .Where(l => l.ProductoId is not null)
                     .Select(l => l.ProductoId!.Value)
                     .Distinct())
        {
            productosMaster[productoId] = await _productos.ObtenerAsync(productoId, cancellationToken);
        }

        var lineas = pedido.Lineas
            .OrderBy(l => l.Posicion)
            .Select(l =>
            {
                var master = l.ProductoId is not null
                    ? productosMaster.GetValueOrDefault(l.ProductoId.Value)
                    : null;
                return new PedidoFacturableLineaDetalle(
                    l.Posicion, l.ProductoId, l.ProductoDescripcion,
                    // Claves SAT: el master vivo manda sobre el snapshot de la
                    // línea — la ingesta congela la clave vigente al importar y
                    // una corrección posterior del catálogo (MTK→H87) debe
                    // reflejarse al facturar. El snapshot queda de fallback
                    // para líneas sin producto en el master.
                    master?.ClaveProdServSat ?? l.ClaveProdServSat,
                    master?.ClaveUnidadSat ?? l.ClaveUnidadSat,
                    l.Cantidad, l.Precio, l.Descuento, l.RequierePedimento,
                    master?.ObjetoImp,
                    // FAC-DET-PR2: la tasa persistida en la línea (documento
                    // A+W o resuelta al capturar) manda sobre la del master.
                    l.TasaIva ?? master?.TasaIvaTraslado,
                    master?.TasaRetencionIva, master?.TasaRetencionIsr);
            })
            .ToList();

        // FAC-ING-PR2: nombre desde el catálogo (antes enum.ToString()). Un
        // id sin fila en el catálogo no bloquea el detalle — display crudo.
        var canalVentaNombre = await _canalesVenta.ObtenerNombreAsync(pedido.CanalVentaId, cancellationToken)
            ?? $"Canal {pedido.CanalVentaId}";

        return new PedidoFacturableDetalleResponse(
            pedido.Id, pedido.NumeroPedido, pedido.Origen.ToString(), pedido.Estado.ToString(),
            pedido.SucursalId, pedido.ClienteId, pedido.ClienteNombre,
            pedido.CanalVentaId, canalVentaNombre, pedido.ComportamientoFiscal.ToString(),
            pedido.Moneda, pedido.ObraId, pedido.ObraNombre, pedido.Comentarios, pedido.ComprobanteVigenteId,
            pedido.Total, pedido.Ranura, pedido.Version, clienteFiscal, lineas);
    }
}

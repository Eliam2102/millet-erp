using MediatR;
using Millet.ComercioExterior.Application.PublicPorts;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
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
/// <summary>
/// 86e3tfdw2: visibilidad de pedidos a crédito por rol. Facturación no
/// referencia Identidad, así que el código se replica aquí (mismo patrón que
/// <see cref="PermisoEmergenciaPedido"/>).
/// </summary>
public static class PermisoPedidoCredito
{
    /// <summary>Espejo de <c>PermisosCanonicos.FacturacionPedidosLeerCredito</c> (Identidad).</summary>
    public const string PermisoLeerCredito = "facturacion.pedidos.leer-credito";
}

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
    IReadOnlyList<PedidoFacturableLineaDetalle> Lineas, EmbarqueExportacionLectura? EmbarqueCe = null,
    // F6.1: clasificación del pedido manual (null fuera de Manual o legacy).
    string? ClaseManual = null,
    string? SubtipoVentaDirecta = null,
    bool EsEmergencia = false,
    string? MotivoEmergencia = null,
    string? ReferenciaPedidoAw = null,
    // 86e3tfdw2: pedido a crédito (condición de pago del origen ≠ CONTADO).
    bool EsCredito = false,
    string? MetodoPagoOrigen = null,
    string? CondicionPagoOrigen = null,
    // 86e3tfd6n: anticipos cobrados con el número de este pedido de A+W y del mismo cliente.
    IReadOnlyList<AnticipoLigadoPedido>? AnticiposLigados = null);

/// <summary>Anticipo cobrado antes de que el pedido llegara al ERP y ligado a él por número de pedido de A+W.</summary>
public sealed record AnticipoLigadoPedido(
    Guid AnticipoId, Guid FacturaAnticipoId, string Folio, decimal MontoCobrado, decimal Saldo, string Estado);

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
    private readonly ICurrentUserPermissions _permisos;
    private readonly IEmbarqueExportacionReadPort? _embarques;

    public PedidoFacturableDetalleHandler(
        FacturacionDbContext db,
        IClientesReadPort clientes,
        IProductosReadPort productos,
        ICanalesVentaReadPort canalesVenta,
        IAlcanceCajaEvaluator alcance,
        ICurrentUserPermissions permisos,
        IEmbarqueExportacionReadPort? embarques = null)
    {
        _db = db;
        _clientes = clientes;
        _productos = productos;
        _canalesVenta = canalesVenta;
        _alcance = alcance;
        _permisos = permisos;
        _embarques = embarques;
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

        // 86e3tfdw2: el pedido a crédito sólo lo ve quien tiene
        // facturacion.pedidos.leer-credito (Contador/Facturación, no el cajero).
        // Sin el permiso → mismo 404 que inexistente, para no revelar que existe.
        if (pedido.EsCredito
            && !await _permisos.TieneAsync(PermisoPedidoCredito.PermisoLeerCredito, cancellationToken))
            throw new EntityNotFoundException("PEDIDO_NO_ENCONTRADO", $"No existe el pedido facturable '{query.Id}'.");

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
            pedido.Total, pedido.Ranura, pedido.Version, clienteFiscal, lineas, _embarques is null ? null : await _embarques.ObtenerAsync(pedido.Id, cancellationToken),
            pedido.ClaseManual?.ToString(), pedido.SubtipoVentaDirecta?.ToString(), pedido.EsEmergencia,
            pedido.MotivoEmergencia, pedido.ReferenciaPedidoAw,
            pedido.EsCredito, pedido.MetodoPagoOrigen, pedido.CondicionPagoOrigen,
            await AnticiposLigadosAsync(pedido, cancellationToken));
    }

    private async Task<IReadOnlyList<AnticipoLigadoPedido>> AnticiposLigadosAsync(PedidoFacturable pedido, CancellationToken ct)
    {
        // Un pedido cancelado en A+W no liga anticipos: quedan libres para el cliente (86e3tfd6n).
        if (pedido.Origen != OrigenPedido.Aw || pedido.Estado == EstadoPedidoFacturable.Cancelado
            || pedido.NumeroPedido is not { } numero) return [];
        var anticipos = await _db.Anticipos.AsNoTracking()
            .Where(a => a.ClienteId == pedido.ClienteId && a.PedidoOrigenRef == numero)
            .Select(a => new { a.Id, a.FacturaAnticipoId, a.MontoCobrado, a.Saldo, a.Estado })
            .ToListAsync(ct);
        var ids = anticipos.Select(a => a.FacturaAnticipoId).ToList();
        var folios = await _db.FacturasAnticipo.AsNoTracking()
            .Where(f => ids.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Folio, ct);
        return [.. anticipos.Select(a => new AnticipoLigadoPedido(
            a.Id, a.FacturaAnticipoId, folios.GetValueOrDefault(a.FacturaAnticipoId) ?? "", a.MontoCobrado, a.Saldo, a.Estado.ToString()))];
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Facturas.Queries;

/// <summary>
/// Bandeja de facturas de venta (§7.2 diseño), con filtro por estado y
/// paginación. CAJAS-PR2: filtrada por la Capa A de Cajas;
/// <paramref name="SoloSinAsignar"/> restringe al bucket "Sin asignar"
/// (requiere <c>facturacion.caja.leer-todas</c>).
///
/// <para>F6.1: cada fila proyecta la clase del pedido manual de origen (venta
/// directa / emergencia) por left join con <c>pedido_facturable</c>;
/// <paramref name="Clase"/> filtra por ella (F6.1-a: facturas de emergencia
/// identificables en la bandeja).</para>
/// </summary>
public sealed record BandejaFacturasQuery(
    EstadoTimbrado? Estado,
    int Offset,
    int Limit,
    bool SoloSinAsignar = false,
    ClasePedidoManual? Clase = null,
    // U-1 (86e3yxn2p): solo facturas con una NC pendiente de timbrar.
    bool SoloNcPendiente = false,
    // Búsqueda en el servidor por folio, UUID, receptor o RFC.
    string? Q = null) : IRequest<BandejaFacturasResponse>;

public sealed record BandejaFacturasResponse(
    IReadOnlyList<FacturaBandejaItem> Items,
    int? SinAsignarCount,
    int Total = 0);

public sealed record FacturaBandejaItem(
    Guid Id,
    string Folio,
    string Estado,
    string? Uuid,
    string ReceptorNombre,
    string ReceptorRfc,
    decimal Total,
    string Moneda,
    DateTimeOffset? FechaTimbrado,
    // F6.1: clase del pedido manual de origen; null si no viene de un pedido
    // manual clasificado (A+W, Planta Pintura, sin pedido o legacy).
    string? ClaseManual = null,
    string? SubtipoVentaDirecta = null,
    bool EsEmergencia = false,
    // U-1: la factura tiene una NC (ranura o anticipo) pendiente de timbrar.
    bool NotaCreditoPendiente = false);

public sealed class BandejaFacturasHandler
    : IRequestHandler<BandejaFacturasQuery, BandejaFacturasResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public BandejaFacturasHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<BandejaFacturasResponse> Handle(
        BandejaFacturasQuery query,
        CancellationToken cancellationToken)
    {
        var q = _db.FacturasVenta.AsNoTracking();
        if (query.Estado is { } estado) q = q.Where(f => f.Estado == estado);
        if (query.Clase is { } clase)
            q = q.Where(f => _db.PedidosFacturables.Any(p =>
                p.Id == f.PedidoFacturableId && p.Origen == OrigenPedido.Manual && p.ClaseManual == clase));

        if (query.SoloNcPendiente)
            q = q.Where(f => _db.NotasCredito.Any(n => n.FacturaRelacionadaId == f.Id
                && (n.Estado == EstadoTimbrado.TimbradoFallido || n.Estado == EstadoTimbrado.TimbradoEnProceso)));

        if (query.Q?.Trim() is { Length: > 0 } texto)
        {
            var patron = PaginacionExtensions.PatronContiene(texto);
            q = q.Where(f => EF.Functions.ILike(f.Folio, patron)
                          || (f.Uuid != null && EF.Functions.ILike(f.Uuid, patron))
                          || EF.Functions.ILike(f.ReceptorNombre, patron)
                          || EF.Functions.ILike(f.ReceptorRfc, patron));
        }

        (q, var sinAsignarCount) = await AlcanceBandejaHelper.AplicarAsync(
            _alcance, q, query.SoloSinAsignar, cancellationToken);

        var pagina = await (
                from f in q
                join p in _db.PedidosFacturables.AsNoTracking()
                    on f.PedidoFacturableId equals (Guid?)p.Id into pj
                from p in pj.DefaultIfEmpty()
                orderby f.FolioNumero descending, f.Id
                select new
                {
                    f.Id, f.Folio, f.Estado, f.Uuid, f.ReceptorNombre,
                    f.ReceptorRfc, f.Total, f.Moneda, f.FechaTimbrado,
                    Origen = p == null ? (OrigenPedido?)null : p.Origen,
                    ClaseManual = p == null ? null : p.ClaseManual,
                    SubtipoVentaDirecta = p == null ? null : p.SubtipoVentaDirecta,
                    NcPendiente = _db.NotasCredito.Any(n => n.FacturaRelacionadaId == f.Id
                        && (n.Estado == EstadoTimbrado.TimbradoFallido || n.Estado == EstadoTimbrado.TimbradoEnProceso)),
                })
            .ToPaginaAsync(query.Offset, query.Limit, cancellationToken);

        var items = pagina.Items
            .Select(r =>
            {
                var clase = r.Origen == OrigenPedido.Manual ? r.ClaseManual : null;
                return new FacturaBandejaItem(
                    r.Id, r.Folio, r.Estado.ToString(), r.Uuid, r.ReceptorNombre,
                    r.ReceptorRfc, r.Total, r.Moneda, r.FechaTimbrado,
                    clase?.ToString(),
                    clase is null ? null : r.SubtipoVentaDirecta?.ToString(),
                    clase == ClasePedidoManual.Emergencia,
                    r.NcPendiente);
            })
            .ToList();

        return new BandejaFacturasResponse(items, sinAsignarCount, pagina.Total);
    }
}

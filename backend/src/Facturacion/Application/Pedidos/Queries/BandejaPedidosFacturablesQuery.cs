using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Pedidos.Queries;

/// <summary>
/// Bandeja de pedidos facturables (§7.2 diseño), con filtros y paginación.
/// CAJAS-PR2: filtrada por la Capa A de Cajas; <paramref name="SoloSinAsignar"/>
/// restringe al bucket "Sin asignar" (requiere <c>facturacion.caja.leer-todas</c>).
/// F6.1: <paramref name="Clase"/> filtra los pedidos manuales por clase
/// (venta directa / emergencia).
/// 86e3tfdw2: los pedidos a crédito sólo los ve quien tiene
/// <c>facturacion.pedidos.leer-credito</c>; <paramref name="Credito"/> filtra
/// por condición de pago (true = crédito, false = contado).
/// </summary>
public sealed record BandejaPedidosFacturablesQuery(
    EstadoPedidoFacturable? Estado,
    OrigenPedido? Origen,
    int Offset,
    int Limit,
    bool SoloSinAsignar = false,
    ClasePedidoManual? Clase = null,
    bool? Credito = null) : IRequest<BandejaPedidosFacturablesResponse>;

public sealed record BandejaPedidosFacturablesResponse(
    IReadOnlyList<PedidoBandejaItem> Items,
    int? SinAsignarCount);

public sealed record PedidoBandejaItem(
    Guid Id,
    string? NumeroPedido,
    string Origen,
    string Estado,
    string ClienteNombre,
    decimal Total,
    string Moneda,
    DateTimeOffset CreatedAt,
    // F6.1: clasificación del pedido manual (null fuera de Manual o legacy).
    string? ClaseManual = null,
    string? SubtipoVentaDirecta = null,
    bool EsEmergencia = false,
    // 86e3tfdw2: pedido a crédito (condición de pago del origen ≠ contado).
    bool EsCredito = false);

public sealed class BandejaPedidosFacturablesHandler
    : IRequestHandler<BandejaPedidosFacturablesQuery, BandejaPedidosFacturablesResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;
    private readonly ICurrentUserPermissions _permisos;

    public BandejaPedidosFacturablesHandler(
        FacturacionDbContext db, IAlcanceCajaEvaluator alcance, ICurrentUserPermissions permisos)
    {
        _db = db;
        _alcance = alcance;
        _permisos = permisos;
    }

    public async Task<BandejaPedidosFacturablesResponse> Handle(
        BandejaPedidosFacturablesQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);

        var q = _db.PedidosFacturables.AsNoTracking();
        if (query.Estado is { } estado) q = q.Where(p => p.Estado == estado);
        if (query.Origen is { } origen) q = q.Where(p => p.Origen == origen);
        if (query.Clase is { } clase)
            q = q.Where(p => p.Origen == OrigenPedido.Manual && p.ClaseManual == clase);
        if (query.Credito is { } credito) q = q.Where(p => p.EsCredito == credito);

        // 86e3tfdw2: sin leer-credito (p. ej. cajero) el pedido a crédito no
        // existe para el usuario. Se filtra ANTES del alcance para que el
        // contador "Sin asignar" tampoco lo cuente.
        if (!await _permisos.TieneAsync(PermisoPedidoCredito.PermisoLeerCredito, cancellationToken))
            q = q.Where(p => !p.EsCredito);

        (q, var sinAsignarCount) = await AlcanceBandejaHelper.AplicarAsync(
            _alcance, q, query.SoloSinAsignar, cancellationToken);

        var rows = await q
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(p => new
            {
                p.Id, p.NumeroPedido, p.Origen, p.Estado, p.ClienteNombre, p.Total, p.Moneda, p.CreatedAt,
                p.ClaseManual, p.SubtipoVentaDirecta, p.EsCredito,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new PedidoBandejaItem(
                r.Id, r.NumeroPedido, r.Origen.ToString(), r.Estado.ToString(),
                r.ClienteNombre, r.Total, r.Moneda, r.CreatedAt,
                r.ClaseManual?.ToString(), r.SubtipoVentaDirecta?.ToString(),
                r.Origen == OrigenPedido.Manual && r.ClaseManual == ClasePedidoManual.Emergencia,
                r.EsCredito))
            .ToList();

        return new BandejaPedidosFacturablesResponse(items, sinAsignarCount);
    }
}

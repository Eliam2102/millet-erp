using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Trazabilidad;

/// <summary>
/// Tipo de nodo del árbol de trazabilidad de Facturación (doc 13 §4.3, 13-D).
/// Los valores 0–4 están reservados para Compras (mirror FE compartido en
/// <c>components/erp/trazabilidad/types.ts</c>); Facturación usa 5–10.
/// El valor numérico es ABI del contrato FE — agregar al final, nunca renumerar.
/// </summary>
public enum TipoNodoTrazabilidadFacturacion : short
{
    PedidoFacturable = 5,
    FacturaVenta = 6,
    FacturaAnticipo = 7,
    NotaCredito = 8,
    ReciboPago = 9,
    CartaPorte = 10,
}

/// <summary>
/// Árbol documento-céntrico de un comprobante o pedido de Facturación
/// (13-D): un nivel de ascendientes (origen) y descendientes (derivados),
/// resuelto desde FKs + relaciones CFDI + <c>ReciboPagoFactura</c> +
/// <c>AnticipoVinculacion</c> + <c>CartaPortePreviaId</c>. La cadena
/// completa se navega saltando de detalle en detalle (el componente FE
/// pinta un nivel por lado). Alcance de cajas: raíz fuera de alcance → 404.
/// </summary>
public sealed record ArbolDocumentosFacturacionQuery(
    TipoNodoTrazabilidadFacturacion TipoRaiz, Guid Id)
    : IRequest<ArbolDocumentosFacturacionResponse>;

public sealed record NodoTrazabilidadFacturacion(
    short Tipo, Guid Id, string Folio, string Estado, string? Uuid,
    decimal? Total, DateTimeOffset? Fecha);

public sealed record ArbolDocumentosFacturacionResponse(
    NodoTrazabilidadFacturacion Actual,
    IReadOnlyList<NodoTrazabilidadFacturacion> Ascendientes,
    IReadOnlyList<NodoTrazabilidadFacturacion> Descendientes);

public sealed class ArbolDocumentosFacturacionHandler
    : IRequestHandler<ArbolDocumentosFacturacionQuery, ArbolDocumentosFacturacionResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public ArbolDocumentosFacturacionHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<ArbolDocumentosFacturacionResponse> Handle(
        ArbolDocumentosFacturacionQuery query, CancellationToken cancellationToken)
    {
        return query.TipoRaiz == TipoNodoTrazabilidadFacturacion.PedidoFacturable
            ? await ArbolDePedidoAsync(query.Id, cancellationToken)
            : await ArbolDeComprobanteAsync(query.Id, cancellationToken);
    }

    // ---- Raíz pedido ----

    private async Task<ArbolDocumentosFacturacionResponse> ArbolDePedidoAsync(
        Guid id, CancellationToken ct)
    {
        var alcance = await _alcance.ResolverAsync(ct);
        var pedido = await alcance.AplicarA(_db.PedidosFacturables.AsNoTracking())
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new EntityNotFoundException(
                "PEDIDO_NO_ENCONTRADO", $"No existe el pedido facturable '{id}'.");

        var descendientes = await ComprobantesDelPedidoAsync(id, ct);
        return new ArbolDocumentosFacturacionResponse(NodoDe(pedido), [], descendientes);
    }

    // ---- Raíz comprobante (despacho por tipo TPT) ----

    private async Task<ArbolDocumentosFacturacionResponse> ArbolDeComprobanteAsync(
        Guid id, CancellationToken ct)
    {
        var alcance = await _alcance.ResolverAsync(ct);
        var c = await alcance.AplicarA(_db.Comprobantes.AsNoTracking())
            .Include(x => x.Relaciones)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante '{id}'.");

        var (asc, desc) = c switch
        {
            FacturaVenta fv => await LadosDeFacturaVentaAsync(fv, ct),
            FacturaAnticipo fa => await LadosDeFacturaAnticipoAsync(fa, ct),
            NotaCredito nc => (await LadosDeNotaCreditoAsync(nc, ct), new List<NodoTrazabilidadFacturacion>()),
            ReciboPago repp => (await LadosDeReppAsync(repp, ct), new List<NodoTrazabilidadFacturacion>()),
            Domain.CartaPorte.CartaPorte cp => await LadosDeCartaPorteAsync(cp, ct),
            _ => (new List<NodoTrazabilidadFacturacion>(), new List<NodoTrazabilidadFacturacion>()),
        };

        return new ArbolDocumentosFacturacionResponse(NodoDe(c), asc, desc);
    }

    private async Task<(List<NodoTrazabilidadFacturacion>, List<NodoTrazabilidadFacturacion>)>
        LadosDeFacturaVentaAsync(FacturaVenta fv, CancellationToken ct)
    {
        var asc = new List<NodoTrazabilidadFacturacion>();
        if (fv.PedidoFacturableId is Guid pedidoId)
            asc.AddRange(await PedidosAsync([pedidoId], ct));

        // Anticipos aplicados: relaciones 07 hacia CFDIs de anticipo (por UUID).
        var uuids07 = fv.Relaciones
            .Where(r => r.TipoRelacion == "07")
            .Select(r => r.UuidRelacionado).Distinct().ToList();
        if (uuids07.Count > 0)
            asc.AddRange(NodosDe(await _db.FacturasAnticipo.AsNoTracking()
                .Where(f => f.Uuid != null && uuids07.Contains(f.Uuid))
                .ToListAsync(ct), TipoNodoTrazabilidadFacturacion.FacturaAnticipo));

        var desc = new List<NodoTrazabilidadFacturacion>();
        desc.AddRange(NodosDe(await _db.NotasCredito.AsNoTracking()
            .Where(n => n.FacturaRelacionadaId == fv.Id)
            .ToListAsync(ct), TipoNodoTrazabilidadFacturacion.NotaCredito));
        desc.AddRange(NodosDe(await _db.RecibosPago.AsNoTracking()
            .Where(r => r.FacturasPagadas.Any(p => p.FacturaVentaId == fv.Id))
            .ToListAsync(ct), TipoNodoTrazabilidadFacturacion.ReciboPago));
        return (asc, desc);
    }

    private async Task<(List<NodoTrazabilidadFacturacion>, List<NodoTrazabilidadFacturacion>)>
        LadosDeFacturaAnticipoAsync(FacturaAnticipo fa, CancellationToken ct)
    {
        var asc = new List<NodoTrazabilidadFacturacion>();
        if (fa.PedidoFacturableId is Guid pedidoId)
            asc.AddRange(await PedidosAsync([pedidoId], ct));

        var desc = new List<NodoTrazabilidadFacturacion>();
        var anticipo = await _db.Anticipos.AsNoTracking()
            .Include(a => a.Vinculaciones)
            .FirstOrDefaultAsync(a => a.Id == fa.AnticipoId, ct);
        if (anticipo is not null && anticipo.Vinculaciones.Count > 0)
        {
            var facturaIds = anticipo.Vinculaciones.Select(v => v.FacturaVentaId).Distinct().ToList();
            var ncIds = anticipo.Vinculaciones
                .Where(v => v.NcAmortizacionId is not null)
                .Select(v => v.NcAmortizacionId!.Value).Distinct().ToList();
            desc.AddRange(NodosDe(await _db.FacturasVenta.AsNoTracking()
                .Where(f => facturaIds.Contains(f.Id)).ToListAsync(ct),
                TipoNodoTrazabilidadFacturacion.FacturaVenta));
            if (ncIds.Count > 0)
                desc.AddRange(NodosDe(await _db.NotasCredito.AsNoTracking()
                    .Where(n => ncIds.Contains(n.Id)).ToListAsync(ct),
                    TipoNodoTrazabilidadFacturacion.NotaCredito));
        }
        return (asc, desc);
    }

    private async Task<List<NodoTrazabilidadFacturacion>> LadosDeNotaCreditoAsync(
        NotaCredito nc, CancellationToken ct)
    {
        var asc = new List<NodoTrazabilidadFacturacion>();
        if (nc.AnticipoOrigenId is Guid anticipoId)
        {
            var facturaAnticipoId = await _db.Anticipos.AsNoTracking()
                .Where(a => a.Id == anticipoId)
                .Select(a => (Guid?)a.FacturaAnticipoId)
                .FirstOrDefaultAsync(ct);
            if (facturaAnticipoId is Guid faId)
                asc.AddRange(NodosDe(await _db.FacturasAnticipo.AsNoTracking()
                    .Where(f => f.Id == faId).ToListAsync(ct),
                    TipoNodoTrazabilidadFacturacion.FacturaAnticipo));
        }
        if (nc.FacturaRelacionadaId is Guid facturaId)
            asc.AddRange(NodosDe(await _db.FacturasVenta.AsNoTracking()
                .Where(f => f.Id == facturaId).ToListAsync(ct),
                TipoNodoTrazabilidadFacturacion.FacturaVenta));
        return asc;
    }

    private async Task<List<NodoTrazabilidadFacturacion>> LadosDeReppAsync(
        ReciboPago repp, CancellationToken ct)
    {
        var facturaIds = await _db.RecibosPago.AsNoTracking()
            .Where(r => r.Id == repp.Id)
            .SelectMany(r => r.FacturasPagadas.Select(p => p.FacturaVentaId))
            .Distinct()
            .ToListAsync(ct);
        return NodosDe(await _db.FacturasVenta.AsNoTracking()
            .Where(f => facturaIds.Contains(f.Id)).ToListAsync(ct),
            TipoNodoTrazabilidadFacturacion.FacturaVenta);
    }

    private async Task<(List<NodoTrazabilidadFacturacion>, List<NodoTrazabilidadFacturacion>)>
        LadosDeCartaPorteAsync(Domain.CartaPorte.CartaPorte cp, CancellationToken ct)
    {
        var asc = new List<NodoTrazabilidadFacturacion>();
        if (cp.PedidoFacturableId is Guid pedidoId)
            asc.AddRange(await PedidosAsync([pedidoId], ct));
        if (cp.CartaPortePreviaId is Guid previaId)
            asc.AddRange(NodosDe(await _db.CartasPorte.AsNoTracking()
                .Where(x => x.Id == previaId).ToListAsync(ct),
                TipoNodoTrazabilidadFacturacion.CartaPorte));

        var desc = NodosDe(await _db.CartasPorte.AsNoTracking()
            .Where(x => x.CartaPortePreviaId == cp.Id).ToListAsync(ct),
            TipoNodoTrazabilidadFacturacion.CartaPorte);
        return (asc, desc);
    }

    private async Task<List<NodoTrazabilidadFacturacion>> ComprobantesDelPedidoAsync(
        Guid pedidoId, CancellationToken ct)
    {
        var nodos = new List<NodoTrazabilidadFacturacion>();
        nodos.AddRange(NodosDe(await _db.FacturasVenta.AsNoTracking()
            .Where(f => f.PedidoFacturableId == pedidoId).ToListAsync(ct),
            TipoNodoTrazabilidadFacturacion.FacturaVenta));
        nodos.AddRange(NodosDe(await _db.FacturasAnticipo.AsNoTracking()
            .Where(f => f.PedidoFacturableId == pedidoId).ToListAsync(ct),
            TipoNodoTrazabilidadFacturacion.FacturaAnticipo));
        nodos.AddRange(NodosDe(await _db.CartasPorte.AsNoTracking()
            .Where(f => f.PedidoFacturableId == pedidoId).ToListAsync(ct),
            TipoNodoTrazabilidadFacturacion.CartaPorte));
        return nodos;
    }

    private async Task<List<NodoTrazabilidadFacturacion>> PedidosAsync(
        IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var pedidos = await _db.PedidosFacturables.AsNoTracking()
            .Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        return pedidos.Select(NodoDe).ToList();
    }

    private static NodoTrazabilidadFacturacion NodoDe(PedidoFacturable p) => new(
        (short)TipoNodoTrazabilidadFacturacion.PedidoFacturable,
        p.Id,
        p.NumeroPedido ?? p.Id.ToString()[..8],
        p.Estado.ToString(),
        Uuid: null,
        Total: null,
        Fecha: null);

    private static NodoTrazabilidadFacturacion NodoDe(Comprobante c) => new(
        (short)TipoDe(c), c.Id, c.Folio, c.Estado.ToString(), c.Uuid, c.Total, c.FechaTimbrado);

    private static List<NodoTrazabilidadFacturacion> NodosDe<T>(
        IEnumerable<T> comprobantes, TipoNodoTrazabilidadFacturacion tipo) where T : Comprobante =>
        comprobantes
            .Select(c => new NodoTrazabilidadFacturacion(
                (short)tipo, c.Id, c.Folio, c.Estado.ToString(), c.Uuid, c.Total, c.FechaTimbrado))
            .ToList();

    private static TipoNodoTrazabilidadFacturacion TipoDe(Comprobante c) => c switch
    {
        FacturaVenta => TipoNodoTrazabilidadFacturacion.FacturaVenta,
        FacturaAnticipo => TipoNodoTrazabilidadFacturacion.FacturaAnticipo,
        NotaCredito => TipoNodoTrazabilidadFacturacion.NotaCredito,
        ReciboPago => TipoNodoTrazabilidadFacturacion.ReciboPago,
        Domain.CartaPorte.CartaPorte => TipoNodoTrazabilidadFacturacion.CartaPorte,
        _ => TipoNodoTrazabilidadFacturacion.FacturaVenta,
    };
}

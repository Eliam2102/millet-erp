using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Trazabilidad;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.Trazabilidad;

/// <summary>
/// Implementación V1 (F7-PR2) de <see cref="IObtenerArbolDocumentosService"/>.
/// Conoce RQ y OC: desde una RQ navega a las OCs que la consumen
/// (downstream); desde una OC navega a las RQs origen (upstream) vía
/// <c>LineaOrdenCompra.RequisicionId</c>.
///
/// <para>
/// El árbol se construye en una sola pasada cuando es factible: para
/// MVP las profundidades son acotadas (RQ → OC → recepción/factura/pago
/// → nada). En F8+, cuando se agreguen providers de CxP/Tesorería, el
/// servicio podría componerse vía estrategia para mantener la
/// implementación O(N) y limitar IO a lo necesario.
/// </para>
/// </summary>
public sealed class ObtenerArbolDocumentosService : IObtenerArbolDocumentosService
{
    private readonly ComprasDbContext _db;
    private readonly IEnumerable<IProveedorNodosTrazabilidad> _providers;

    public ObtenerArbolDocumentosService(
        ComprasDbContext db,
        IEnumerable<IProveedorNodosTrazabilidad> providers)
    {
        _db = db;
        _providers = providers;
    }

    private async Task<List<NodoArbolDocumento>> RecolectarDescendentesAsync(
        TipoDocumentoTrazabilidad tipoOrigen,
        Guid idOrigen,
        CancellationToken ct)
    {
        var aggregate = new List<NodoArbolDocumento>();
        foreach (var provider in _providers)
        {
            var nodos = await provider.ObtenerDescendentesAsync(tipoOrigen, idOrigen, ct);
            if (nodos.Count > 0) aggregate.AddRange(nodos);
        }
        return aggregate;
    }

    public async Task<NodoArbolDocumento?> ObtenerAsync(
        TipoDocumentoTrazabilidad tipoDocumento,
        Guid id,
        CancellationToken cancellationToken)
    {
        return tipoDocumento switch
        {
            TipoDocumentoTrazabilidad.Requisicion => await ConstruirDesdeRqAsync(id, cancellationToken),
            TipoDocumentoTrazabilidad.OrdenCompra => await ConstruirDesdeOcAsync(id, cancellationToken),
            _ => null,
        };
    }

    private async Task<NodoArbolDocumento?> ConstruirDesdeRqAsync(Guid rqId, CancellationToken ct)
    {
        var rq = await _db.Requisiciones
            .AsNoTracking()
            .Where(r => r.Id == rqId)
            .Select(r => new
            {
                r.Id,
                FolioValor = r.Folio.Valor,
                r.Estado,
                r.FechaSolicitud,
            })
            .FirstOrDefaultAsync(ct);

        if (rq is null) return null;

        // Descendentes: OCs que consumen esta RQ (al menos una línea
        // referencia la RQ).
        var ocIds = await _db.LineasOrdenCompra
            .AsNoTracking()
            .Where(l => l.RequisicionId == rqId)
            .Select(l => l.OrdenCompraId)
            .Distinct()
            .ToListAsync(ct);

        // FechaDocumento es DateOnly (ADR-0040). El nodo del árbol usa un
        // DateTimeOffset (campo unión heterogéneo: otros documentos son
        // instantes reales), así que materializamos y convertimos la fecha de
        // calendario de la OC a su medianoche-local-de-México en memoria —
        // EF no traduce la conversión de TZ.
        var ocs = ocIds.Count == 0
            ? new List<NodoArbolDocumento>()
            : (await _db.OrdenesCompra
                .AsNoTracking()
                .Where(o => ocIds.Contains(o.Id))
                .Select(o => new
                {
                    o.Id,
                    Folio = o.Folio.Valor,
                    o.Estado,
                    o.FechaDocumento,
                })
                .ToListAsync(ct))
                .Select(o => new NodoArbolDocumento(
                    TipoDocumentoTrazabilidad.OrdenCompra,
                    o.Id,
                    o.Folio,
                    o.Estado.ToString(),
                    FechaContable.InicioDelDiaUtc(o.FechaDocumento),
                    new List<NodoArbolDocumento>(),
                    new List<NodoArbolDocumento>()))
                .ToList();

        return new NodoArbolDocumento(
            TipoDocumentoTrazabilidad.Requisicion,
            rq.Id,
            rq.FolioValor,
            rq.Estado.ToString(),
            rq.FechaSolicitud,
            Ascendentes: new List<NodoArbolDocumento>(),
            Descendentes: ocs);
    }

    private async Task<NodoArbolDocumento?> ConstruirDesdeOcAsync(Guid ocId, CancellationToken ct)
    {
        var oc = await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Id == ocId)
            .Select(o => new
            {
                o.Id,
                FolioValor = o.Folio.Valor,
                o.Estado,
                o.FechaDocumento,
            })
            .FirstOrDefaultAsync(ct);

        if (oc is null) return null;

        // Ascendentes: RQs distintas referenciadas por las líneas.
        var rqIds = await _db.LineasOrdenCompra
            .AsNoTracking()
            .Where(l => l.OrdenCompraId == ocId && l.RequisicionId != null)
            .Select(l => l.RequisicionId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var rqs = rqIds.Count == 0
            ? new List<NodoArbolDocumento>()
            : await _db.Requisiciones
                .AsNoTracking()
                .Where(r => rqIds.Contains(r.Id))
                .Select(r => new NodoArbolDocumento(
                    TipoDocumentoTrazabilidad.Requisicion,
                    r.Id,
                    r.Folio.Valor,
                    r.Estado.ToString(),
                    r.FechaSolicitud,
                    new List<NodoArbolDocumento>(),
                    new List<NodoArbolDocumento>()))
                .ToListAsync(ct);

        // Descendentes: agregados por los providers cross-módulo
        // (Almacén → recepciones, CxP → facturas, Tesorería → pagos en
        // el futuro). Cada provider devuelve lo suyo o lista vacía.
        var descendentes = await RecolectarDescendentesAsync(
            TipoDocumentoTrazabilidad.OrdenCompra, ocId, ct);

        return new NodoArbolDocumento(
            TipoDocumentoTrazabilidad.OrdenCompra,
            oc.Id,
            oc.FolioValor,
            oc.Estado.ToString(),
            FechaContable.InicioDelDiaUtc(oc.FechaDocumento),
            Ascendentes: rqs,
            Descendentes: descendentes);
    }
}

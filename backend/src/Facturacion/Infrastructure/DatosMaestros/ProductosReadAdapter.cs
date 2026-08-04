using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Domain.Ports;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.DatosMaestros;

/// <summary>
/// Adapter REAL de <see cref="IProductosReadPort"/> sobre el master de
/// productos de venta (<c>compartido.producto_aw</c>, ADR-0048 D5 —
/// SEPARADO del Articulo de compras/almacén). Cierra
/// PLATFORM-TODO(&lt;DatosMaestrosFiscal&gt;) para productos.
/// <c>Origen</c> se proyecta como string (contrato del puerto).
/// </summary>
public sealed class ProductosReadAdapter : IProductosReadPort
{
    private readonly CompartidoDbContext _db;

    public ProductosReadAdapter(CompartidoDbContext db) => _db = db;

    public async Task<ProductoFiscalLectura?> ObtenerAsync(
        Guid productoId, CancellationToken cancellationToken)
    {
        var p = await _db.ProductosAw.AsNoTracking()
            .Where(x => x.Id == productoId)
            .Select(x => new
            {
                x.Id, x.Descripcion, x.ClaveProdServSat, x.ClaveUnidadSat,
                x.ObjetoImp, x.TasaIvaTraslado, x.TasaRetencionIva,
                x.TasaRetencionIsr, x.Origen,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return p is null ? null : new ProductoFiscalLectura(
            p.Id, p.Descripcion, p.ClaveProdServSat, p.ClaveUnidadSat,
            p.ObjetoImp, p.TasaIvaTraslado, p.TasaRetencionIva,
            p.TasaRetencionIsr, p.Origen.ToString());
    }

    public async Task<ProductoFiscalLectura?> ResolverPorReferenciaAsync(
        string referenciaExterna, CancellationToken cancellationToken)
    {
        var p = await _db.ProductosAw.AsNoTracking()
            .Where(x => x.ReferenciaExterna == referenciaExterna)
            .Select(x => new
            {
                x.Id, x.Descripcion, x.ClaveProdServSat, x.ClaveUnidadSat,
                x.ObjetoImp, x.TasaIvaTraslado, x.TasaRetencionIva,
                x.TasaRetencionIsr, x.Origen,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return p is null ? null : new ProductoFiscalLectura(
            p.Id, p.Descripcion, p.ClaveProdServSat, p.ClaveUnidadSat,
            p.ObjetoImp, p.TasaIvaTraslado, p.TasaRetencionIva,
            p.TasaRetencionIsr, p.Origen.ToString());
    }

    public async Task<IReadOnlyList<ProductoAwBusquedaItem>> BuscarAsync(
        string? referencia, string? descripcion, int limit, CancellationToken cancellationToken)
    {
        IQueryable<ProductoAw> q = _db.ProductosAw.AsNoTracking()
            .Where(p => p.Estatus == EstatusCatalogo.Activo);

        // Filtros excluyentes al estilo ADR-0045 (referencia tiene precedencia).
#pragma warning disable CA1304, CA1311, CA1862
        if (!string.IsNullOrWhiteSpace(referencia))
        {
            q = q.Where(p => p.ReferenciaExterna.ToLower().Contains(referencia.ToLower()));
        }
        else if (!string.IsNullOrWhiteSpace(descripcion))
        {
            q = q.Where(p =>
                PostgresFunctions.Translate(p.Descripcion.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)
                    .Contains(PostgresFunctions.Translate(descripcion.ToLower(), PostgresFunctions.AcentosOrigen, PostgresFunctions.AcentosDestino)));
        }
#pragma warning restore CA1304, CA1311, CA1862

        return await q
            .OrderBy(p => p.ReferenciaExterna)
            .Take(limit)
            .Select(p => new ProductoAwBusquedaItem(
                p.Id, p.ReferenciaExterna, p.Descripcion, p.UnidadMedida,
                p.ClaveProdServSat, p.ClaveUnidadSat, p.ObjetoImp,
                p.TasaIvaTraslado, p.TasaRetencionIva, p.TasaRetencionIsr,
                p.FraccionArancelaria, p.UnidadAduana, p.PesoUnitarioKg))
            .ToListAsync(cancellationToken);
    }
}

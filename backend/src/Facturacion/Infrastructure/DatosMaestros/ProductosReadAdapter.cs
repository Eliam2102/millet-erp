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
// Normalización en SQL; ToUpperInvariant no se traduce en este proveedor.
#pragma warning disable CA1304, CA1311, CA1862
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
                x.TasaRetencionIsr, x.Origen, x.Tipo, x.EstadoFiscal, x.PesoUnitarioKg,
                x.EsServicioRecubrimiento, Activo = x.Estatus == EstatusCatalogo.Activo,
                x.FraccionArancelaria, x.UnidadAduana, x.UnidadMedidaId, x.UnidadMedida,
                DecimalesCantidad = _db.UnidadesMedida
                    .Where(u => u.Id == x.UnidadMedidaId || (x.UnidadMedidaId == null
                        && u.Codigo.Trim().ToUpper().TrimEnd('.') == x.UnidadMedida.Trim().ToUpper().TrimEnd('.')))
                    .Select(u => (int?)u.Decimales).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return p is null ? null : new ProductoFiscalLectura(
            p.Id, p.Descripcion, p.ClaveProdServSat, p.ClaveUnidadSat,
            p.ObjetoImp, p.TasaIvaTraslado, p.TasaRetencionIva,
            p.TasaRetencionIsr, p.Origen.ToString(), p.Tipo, p.EstadoFiscal.ToString(), p.PesoUnitarioKg,
            p.EsServicioRecubrimiento, p.Activo, p.FraccionArancelaria, p.UnidadAduana,
            p.UnidadMedidaId, p.UnidadMedida, p.DecimalesCantidad);
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
                x.TasaRetencionIsr, x.Origen, x.Tipo, x.EstadoFiscal, x.PesoUnitarioKg,
                x.EsServicioRecubrimiento, Activo = x.Estatus == EstatusCatalogo.Activo,
                x.FraccionArancelaria, x.UnidadAduana, x.UnidadMedidaId, x.UnidadMedida,
                DecimalesCantidad = _db.UnidadesMedida
                    .Where(u => u.Id == x.UnidadMedidaId || (x.UnidadMedidaId == null
                        && u.Codigo.Trim().ToUpper().TrimEnd('.') == x.UnidadMedida.Trim().ToUpper().TrimEnd('.')))
                    .Select(u => (int?)u.Decimales).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return p is null ? null : new ProductoFiscalLectura(
            p.Id, p.Descripcion, p.ClaveProdServSat, p.ClaveUnidadSat,
            p.ObjetoImp, p.TasaIvaTraslado, p.TasaRetencionIva,
            p.TasaRetencionIsr, p.Origen.ToString(), p.Tipo, p.EstadoFiscal.ToString(), p.PesoUnitarioKg,
            p.EsServicioRecubrimiento, p.Activo, p.FraccionArancelaria, p.UnidadAduana,
            p.UnidadMedidaId, p.UnidadMedida, p.DecimalesCantidad);
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


        return await q
            .OrderBy(p => p.ReferenciaExterna)
            .Take(limit)
            .Select(p => new ProductoAwBusquedaItem(
                p.Id, p.ReferenciaExterna, p.Descripcion, p.UnidadMedida,
                p.ClaveProdServSat, p.ClaveUnidadSat, p.ObjetoImp,
                p.TasaIvaTraslado, p.TasaRetencionIva, p.TasaRetencionIsr,
                p.FraccionArancelaria, p.UnidadAduana, p.PesoUnitarioKg, p.UnidadMedidaId,
                _db.UnidadesMedida.Where(u => u.Id == p.UnidadMedidaId || (p.UnidadMedidaId == null
                    && u.Codigo.Trim().ToUpper().TrimEnd('.') == p.UnidadMedida.Trim().ToUpper().TrimEnd('.')))
                    .Select(u => (int?)u.Decimales).FirstOrDefault()))
            .ToListAsync(cancellationToken);
    }
}

#pragma warning restore CA1304, CA1311, CA1862

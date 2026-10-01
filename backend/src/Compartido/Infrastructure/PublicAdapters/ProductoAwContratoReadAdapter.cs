using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.ProductosAw;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>Adaptador del contrato de producto A+W sobre <see cref="CompartidoDbContext"/> (solo lectura).</summary>
public sealed class ProductoAwContratoReadAdapter(CompartidoDbContext db) : IProductoAwContratoReadPort
{
    public Task<ProductoAwContratoV1?> ObtenerPorReferenciaAsync(string referencia, CancellationToken ct)
    {
        var r = referencia?.Trim();
        return string.IsNullOrEmpty(r)
            ? Task.FromResult<ProductoAwContratoV1?>(null)
            : Proyectar(db.ProductosAw.Where(p => p.ReferenciaExterna == r), ct);
    }

    public Task<ProductoAwContratoV1?> ObtenerPorIdAsync(Guid id, CancellationToken ct) =>
        Proyectar(db.ProductosAw.Where(p => p.Id == id), ct);

    private static async Task<ProductoAwContratoV1?> Proyectar(IQueryable<DatosMaestros.Domain.ProductoAw> q, CancellationToken ct)
    {
        var p = await q.AsNoTracking().Include(x => x.Variantes).FirstOrDefaultAsync(ct);
        return p is null ? null : new ProductoAwContratoV1(
            ProductoAwContratoV1.VersionActual, p.Id, p.ReferenciaExterna, p.Descripcion, p.UnidadMedida,
            p.ClaveProdServSat, p.ClaveUnidadSat, p.ObjetoImp,
            p.TasaIvaTraslado, p.TasaRetencionIva, p.TasaRetencionIsr,
            p.Estatus.ToString(), p.FechaBaja,
            p.Variantes.OrderBy(v => v.ClaveVariante)
                .Select(v => new ProductoAwVarianteContratoV1(v.ClaveVariante, v.AltoMm, v.AnchoMm, v.EspesorMm, v.Composicion))
                .ToList(),
            p.Version);
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.PublicPorts;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

public sealed class CostoMedioReader(AlmacenDbContext db) : ICostoMedioReader
{
    public async Task<decimal> EstimarTrasCompraAsync(Guid sucursalId, Guid? almacenId, Guid articuloId, Guid? varianteId, decimal piezasOM2Entran,
        decimal costoCompra, decimal tipoCambio, CancellationToken ct)
    {
        var costoMxn = CostoMedio.AMxn(costoCompra, tipoCambio);
        var saldos = await (from s in db.SaldosInventario.AsNoTracking()
                            join sub in db.SubAlmacenes on s.SubAlmacenId equals sub.Id
                            join alm in db.Almacenes on sub.AlmacenId equals alm.Id
                            where s.ArticuloId == articuloId && s.ProductoAwVarianteId == varianteId
                                && sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                                && alm.SucursalId == sucursalId && (almacenId == null || alm.Id == almacenId)
                            select new { s.Cantidad, s.CostoPromedioMxn, s.UltimaActualizacionAt }).ToListAsync(ct);
        var (existencia, promedio) = CostoMedio.Vigente(saldos.Select(x => (x.Cantidad, x.CostoPromedioMxn, x.UltimaActualizacionAt)).ToList());
        return CostoMedio.Calcular(existencia, promedio ?? 0m, piezasOM2Entran, costoMxn);
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ProductoTerminado.Infrastructure.PublicAdapters;

/// <summary>
/// Sucursal de piezas y escaneos de PT para <c>DocumentoSucursalScope</c> (ADR-0051).
/// La pieza usa su sucursal física; un escaneo sin sucursal resuelta devuelve lista vacía
/// (el guard lanza SUCURSAL_NO_DETERMINADA y solo el alcance corporativo lo ve).
/// </summary>
public sealed class ProductoTerminadoSucursalReadAdapter(ProductoTerminadoDbContext db) : IProductoTerminadoSucursalReadPort
{
    public async Task<DocumentoSucursales?> ObtenerAsync(string tipo, Guid id, CancellationToken ct)
    {
        if (tipo == "pieza_pt")
        {
            var pieza = await db.Piezas.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.SucursalId }).SingleOrDefaultAsync(ct);
            return pieza is null ? null : new DocumentoSucursales(pieza.Id, [pieza.SucursalId]);
        }
        if (tipo != "escaneo_pt") return null;
        var escaneo = await db.Escaneos.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.SucursalId }).SingleOrDefaultAsync(ct);
        return escaneo is null ? null : new DocumentoSucursales(escaneo.Id, escaneo.SucursalId is Guid s ? [s] : []);
    }

    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        if (tipo == "pieza_pt")
        {
            var piezas = await db.Piezas.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct);
            return piezas.Select(x => new DocumentoSucursales(x.Id, [x.SucursalId])).ToArray();
        }
        if (tipo != "escaneo_pt") return [];
        var escaneos = await db.Escaneos.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct);
        return escaneos.Select(x => new DocumentoSucursales(x.Id, x.SucursalId is Guid s ? [s] : [])).ToArray();
    }
}

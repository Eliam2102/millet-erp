using Microsoft.EntityFrameworkCore;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.ProductoTerminado.Infrastructure.PublicAdapters;
public sealed class ProductoTerminadoSucursalReadAdapter(ProductoTerminadoDbContext db) : IProductoTerminadoSucursalReadPort
{
    public async Task<DocumentoSucursales?> ObtenerAsync(string tipo, Guid id, CancellationToken ct)
    {
        if (tipo == "pieza_pt")
        {
            var piece = await db.Pieces.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return piece is null ? null : new(piece.Id, [piece.SucursalId]);
        }
        var scan = tipo == "escaneo_pt" ? await db.Scans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) : null;
        return scan is null ? null : new(scan.Id, scan.SucursalId is Guid branch ? [branch] : []);
    }
    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        if (tipo == "pieza_pt") return (await db.Pieces.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct))
            .Select(x => new DocumentoSucursales(x.Id, [x.SucursalId])).ToArray();
        if (tipo != "escaneo_pt") return [];
        return (await db.Scans.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct))
            .Select(x => new DocumentoSucursales(x.Id, x.SucursalId is Guid id ? [id] : [])).ToArray();
    }
}

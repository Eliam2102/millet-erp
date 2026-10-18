using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class ComercioExteriorSucursalReadAdapter(ComercioExteriorDbContext db) : IComercioExteriorSucursalReadPort
{
    public async Task<DocumentoSucursales?> ObtenerAsync(string tipo, Guid id, CancellationToken ct)
    {
        if (tipo != "expediente_ce") return null;
        var dato = await db.Expedientes.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.Id, x.SucursalId }).SingleOrDefaultAsync(ct);
        return dato is null ? null : new DocumentoSucursales(dato.Id, [dato.SucursalId]);
    }

    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        if (tipo != "expediente_ce")
            return[];
        var datos = await db.Expedientes.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct);
        return datos.Select(x => new DocumentoSucursales(x.Id, [x.SucursalId])).ToArray();
    }
}

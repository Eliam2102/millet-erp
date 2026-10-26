using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.PublicPorts;
using Millet.Almacen.Infrastructure.Persistence;
namespace Millet.Almacen.Infrastructure.PublicAdapters;
public sealed class RecepcionesImportacionReadAdapter(AlmacenDbContext db) : IRecepcionesImportacionReadPort
{
    public async Task<IReadOnlyList<RecepcionImportacionLectura>> ObtenerPorOcAsync(Guid ocId, CancellationToken ct) =>
        await db.RecepcionesImportacion.AsNoTracking().Where(x => x.OrdenCompraId == ocId).OrderBy(x => x.CreatedAt)
            .Select(x => new RecepcionImportacionLectura(x.RecepcionId, x.NumeroPedimento, x.NumeroBl, x.NumeroContenedor,
                x.Aduana, x.ClavePedimento, x.Partidas.Select(p => new PartidaImportacionLectura(p.LineaOcId, p.FraccionArancelaria)).ToList()))
            .ToListAsync(ct);
}

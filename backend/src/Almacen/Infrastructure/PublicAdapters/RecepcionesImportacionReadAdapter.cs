using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.PublicPorts;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Ports;
using Millet.SharedKernel.Application.UnidadesMedida;
namespace Millet.Almacen.Infrastructure.PublicAdapters;
public sealed class RecepcionesImportacionReadAdapter(AlmacenDbContext db, IComprasOcReadPort compras, IConversionUnidadPort conversion) : IRecepcionesImportacionReadPort
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> ObtenerCantidadesRecibidasAsync(Guid ocId, CancellationToken ct) =>
        await RecibidoPorLineaOc.CalcularAsync(db, conversion, await compras.ObtenerAsync(ocId, ct), ct);

    public async Task<IReadOnlyList<RecepcionImportacionLectura>> ObtenerPorOcAsync(Guid ocId, CancellationToken ct) =>
        await db.RecepcionesImportacion.AsNoTracking().Where(x => x.OrdenCompraId == ocId).OrderBy(x => x.CreatedAt)
            .Select(x => new RecepcionImportacionLectura(x.RecepcionId, x.NumeroPedimento, x.NumeroBl, x.NumeroContenedor,
                x.Aduana, x.ClavePedimento, x.Partidas.Select(p => new PartidaImportacionLectura(p.LineaOcId, p.FraccionArancelaria)).ToList()))
            .ToListAsync(ct);
}

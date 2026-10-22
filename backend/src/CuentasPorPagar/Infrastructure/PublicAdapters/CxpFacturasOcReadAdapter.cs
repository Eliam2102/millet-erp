using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Ports.CuentasPorPagar;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IFacturasOcReadPort"/> (el puerto vive en Compras; CxP ya
/// referencia Compras, no al revés). Conserva el filtro de empresa de CxP.
/// </summary>
public sealed class CxpFacturasOcReadAdapter : IFacturasOcReadPort
{
    private readonly CuentasPorPagarDbContext _db;

    public CxpFacturasOcReadAdapter(CuentasPorPagarDbContext db) => _db = db;

    public async Task<IReadOnlyList<FacturaOcLectura>> ListarPorOcAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken ct)
    {
        if (ocIds.Count == 0)
        {
            return [];
        }

        var ids = ocIds.Distinct().ToArray();
        var filas = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => f.OrdenCompraId != null && ids.Contains(f.OrdenCompraId.Value))
            .OrderBy(f => f.FechaDocumento)
            .Select(f => new
            {
                f.Id,
                OcId = f.OrdenCompraId!.Value,
                f.SerieProveedor,
                f.FolioProveedor,
                f.FechaDocumento,
                f.Moneda,
                f.Total,
                f.DiferenciaContraOc,
                f.Estado,
            })
            .ToListAsync(ct);

        return filas
            .Select(f => new FacturaOcLectura(
                f.Id, f.OcId, f.SerieProveedor, f.FolioProveedor,
                DateOnly.FromDateTime(f.FechaDocumento.Date), f.Moneda, f.Total,
                f.DiferenciaContraOc, f.Estado.ToString()))
            .ToList();
    }
}

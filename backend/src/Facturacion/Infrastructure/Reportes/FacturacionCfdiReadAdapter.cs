using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.Reportes;

/// <summary>
/// Adapter real de <see cref="IFacturacionCfdiReadPort"/> sobre
/// <see cref="FacturacionDbContext"/> (F11). Lo registra
/// <c>AddFacturacionModule</c> para que Obras (u otros) consulten los CFDIs.
/// </summary>
public sealed class FacturacionCfdiReadAdapter : IFacturacionCfdiReadPort
{
    private readonly FacturacionDbContext _db;

    public FacturacionCfdiReadAdapter(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<CfdiObraLectura>> ObtenerPorObraAsync(long obraId, CancellationToken cancellationToken)
    {
        var rows = await _db.FacturasVenta.AsNoTracking()
            .Where(f => f.ObraId == obraId)
            .OrderByDescending(f => f.FolioNumero)
            .Select(f => new { f.Id, f.Folio, f.Uuid, f.Total, f.Moneda, f.Estado, f.FechaTimbrado })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new CfdiObraLectura(r.Id, r.Folio, r.Uuid, r.Total, r.Moneda, r.Estado.ToString(), r.FechaTimbrado))
            .ToList();
    }
}

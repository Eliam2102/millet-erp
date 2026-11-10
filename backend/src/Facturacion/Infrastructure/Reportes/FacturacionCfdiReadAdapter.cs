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

    public async Task<IReadOnlyList<CfdiEmitidoLectura>> ObtenerPorUuidsAsync(IReadOnlyCollection<string> uuids, CancellationToken cancellationToken)
    {
        // El UUID se guarda como lo devuelve el PAC; se busca en mayúsculas y en minúsculas para usar el índice sin funciones en SQL.
        var lista = uuids.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => u.Trim())
            .SelectMany(u => new[] { u.ToUpperInvariant(), u.ToLowerInvariant() }).Distinct().ToList();
        if (lista.Count == 0) return [];
        var rows = await _db.Comprobantes.AsNoTracking()
            .Where(c => c.Uuid != null && lista.Contains(c.Uuid))
            .Select(c => new { c.Uuid, c.ReceptorRfc, c.Total, c.Moneda, c.TipoCambio })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new CfdiEmitidoLectura(r.Uuid!.ToUpperInvariant(), r.ReceptorRfc, r.Total, r.Moneda, r.TipoCambio)).ToList();
    }
}

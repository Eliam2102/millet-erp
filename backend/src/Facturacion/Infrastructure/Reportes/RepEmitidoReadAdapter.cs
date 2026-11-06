using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.Reportes;

/// <summary>K10.7 · facturas de venta incluidas en un REP timbrado (los cancelados no cuentan).</summary>
public sealed class RepEmitidoReadAdapter(FacturacionDbContext db) : IRepEmitidoReadPort
{
    public async Task<IReadOnlyCollection<Guid>> FacturasConRepAsync(IReadOnlyCollection<Guid> facturaVentaIds, CancellationToken ct)
    {
        if (facturaVentaIds.Count == 0) return [];
        return await db.RecibosPago.AsNoTracking().Where(r => r.Estado == EstadoTimbrado.Timbrado)
            .SelectMany(r => r.FacturasPagadas).Where(f => facturaVentaIds.Contains(f.FacturaVentaId))
            .Select(f => f.FacturaVentaId).Distinct().ToListAsync(ct);
    }
}

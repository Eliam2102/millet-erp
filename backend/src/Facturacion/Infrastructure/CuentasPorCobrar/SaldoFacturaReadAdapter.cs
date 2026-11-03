using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.CuentasPorCobrar;

public sealed class SaldoFacturaReadAdapter(CuentasPorCobrarDbContext db) : ISaldoFacturaReadPort
{
    public async Task<decimal?> ObtenerSaldoAsync(Guid facturaVentaId, CancellationToken cancellationToken) =>
        await db.FacturasCartera.AsNoTracking()
            .Where(f => f.FacturaVentaId == facturaVentaId)
            .Select(f => (decimal?)(f.Total - f.MontoPagado - f.MontoNc))
            .FirstOrDefaultAsync(cancellationToken);
}

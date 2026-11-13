using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.CuentasPorCobrar;

/// <summary>H-U41: suma lo aplicado por propuestas abiertas de CxC (mismo criterio que <c>EsAbierta</c> de CxC).</summary>
public sealed class ReservaCobroCxcReadAdapter(CuentasPorCobrarDbContext db) : IReservaCobroCxcReadPort
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> ReservadoPorFacturaAsync(
        IReadOnlyCollection<Guid> facturaVentaIds, CancellationToken cancellationToken)
    {
        if (facturaVentaIds.Count == 0) return new Dictionary<Guid, decimal>();

        var filas = await db.PropuestasAplicacionPago.AsNoTracking()
            .Where(p => p.Estado == EstadoPropuestaAplicacion.Propuesta
                || (p.Estado == EstadoPropuestaAplicacion.Confirmada && !p.ReppTimbrado))
            .SelectMany(p => p.Facturas)
            .Join(db.FacturasCartera.AsNoTracking().Where(c => facturaVentaIds.Contains(c.FacturaVentaId)),
                l => l.FacturaCarteraId, c => c.Id, (l, c) => new { c.FacturaVentaId, l.ImporteAplicado })
            .ToListAsync(cancellationToken);

        return filas.GroupBy(f => f.FacturaVentaId).ToDictionary(g => g.Key, g => g.Sum(f => f.ImporteAplicado));
    }
}

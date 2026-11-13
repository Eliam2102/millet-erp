using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.CuentasPorCobrar;

/// <summary>H-U41: suma lo aplicado por propuestas abiertas de CxC (abiertas como en <c>EsAbierta</c> de CxC, más las PUE confirmadas).</summary>
public sealed class ReservaCobroCxcReadAdapter(CuentasPorCobrarDbContext db) : IReservaCobroCxcReadPort
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> ReservadoPorFacturaAsync(
        IReadOnlyCollection<Guid> facturaVentaIds, CancellationToken cancellationToken)
    {
        if (facturaVentaIds.Count == 0) return new Dictionary<Guid, decimal>();

        // Propuesta en revisión o confirmada sin REP. Una PUE confirmada cuenta siempre: no lleva REP (solo PPD) y el
        // pago ya entró por banco, así que la caja nunca debe volver a cobrarla.
        var filas = await db.PropuestasAplicacionPago.AsNoTracking()
            .Where(p => p.Estado == EstadoPropuestaAplicacion.Propuesta || p.Estado == EstadoPropuestaAplicacion.Confirmada)
            .SelectMany(p => p.Facturas, (p, l) => new { p.Estado, p.ReppTimbrado, l.FacturaCarteraId, l.ImporteAplicado })
            .Join(db.FacturasCartera.AsNoTracking().Where(c => facturaVentaIds.Contains(c.FacturaVentaId)),
                l => l.FacturaCarteraId, c => c.Id,
                (l, c) => new { c.FacturaVentaId, c.MetodoPago, l.Estado, l.ReppTimbrado, l.ImporteAplicado })
            .Where(x => x.Estado == EstadoPropuestaAplicacion.Propuesta || !x.ReppTimbrado || x.MetodoPago != "PPD")
            .ToListAsync(cancellationToken);

        return filas.GroupBy(f => f.FacturaVentaId).ToDictionary(g => g.Key, g => g.Sum(f => f.ImporteAplicado));
    }
}

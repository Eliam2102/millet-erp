using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>K10.7 · lectura de método de pago y REP recibido de las facturas de proveedor (dueño: CxP).</summary>
public sealed class FacturaProveedorFiscalReadAdapter(CuentasPorPagarDbContext db) : IFacturaProveedorFiscalReadPort
{
    public async Task<IReadOnlyList<FacturaProveedorFiscal>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.FacturasProveedor.AsNoTracking().Where(f => ids.Contains(f.Id))
            .Select(f => new FacturaProveedorFiscal(f.Id, f.UuidCfdi, f.MetodoPago, f.ReppRecibido)).ToListAsync(ct);
    }
}

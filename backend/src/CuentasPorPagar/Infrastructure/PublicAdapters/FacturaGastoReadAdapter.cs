using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
public sealed class FacturaGastoReadAdapter(CuentasPorPagarDbContext db) : IFacturaGastoReadPort
{
    public async Task<FacturaGastoLectura?> BuscarAsync(Guid proveedorId, string referencia, Guid? id, CancellationToken ct)
    {
        var factura = await db.Set<FacturaProveedor>().AsNoTracking().Where(f => f.ProveedorId == proveedorId && f.Estado != EstadoPasivo.Cancelada &&
            (id != null ? f.Id == id : f.UuidCfdi == referencia || f.FolioProveedor == referencia)).OrderByDescending(f => f.CreatedAt).FirstOrDefaultAsync(ct);
        return factura is null ? null : new(factura.Id, factura.ProveedorId, factura.SucursalId, factura.Moneda,
            DateOnly.FromDateTime(factura.FechaDocumento.Date), factura.UuidCfdi ?? factura.FolioProveedor ?? referencia);
    }
}

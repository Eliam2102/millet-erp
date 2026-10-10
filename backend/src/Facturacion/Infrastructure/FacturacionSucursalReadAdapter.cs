using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.Facturacion.Infrastructure;
public sealed class FacturacionSucursalReadAdapter(FacturacionDbContext db) : IFacturacionSucursalReadPort
{
    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        var filas = tipo == "sesion_caja"
            ? await db.CajaSesiones.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct)
            : await db.Comprobantes.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct);
        return filas.Select(x => new DocumentoSucursales(x.Id, new[] { x.SucursalId })).ToArray();
    }
}

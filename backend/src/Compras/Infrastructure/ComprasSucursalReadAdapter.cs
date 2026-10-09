using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
namespace Millet.Compras.Infrastructure;
public sealed class ComprasSucursalReadAdapter(ComprasDbContext db) : IComprasSucursalReadPort
{
    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        var filas = tipo == "requisicion"
            ? await db.Requisiciones.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct)
            : await db.OrdenesCompra.AsNoTracking().Select(x => new { x.Id, SucursalId = x.SucursalDestinoId }).ToListAsync(ct);
        return filas.Select(x => new DocumentoSucursales(x.Id, new[] { x.SucursalId })).ToArray();
    }
}

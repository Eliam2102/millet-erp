using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Application.PublicPorts;
using Millet.Tesoreria.Application.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;
namespace Millet.Tesoreria.Infrastructure.PublicAdapters;
public sealed class AutorizacionPagoCeReadAdapter(TesoreriaDbContext db) : IAutorizacionPagoCeReadPort
{
    public Task<decimal> PagadoAsync(Guid anticipoId, CancellationToken ct)
        => db.AplicacionesPagoProveedor.Where(a => a.FacturaProveedorId == anticipoId && !a.Revertida).SumAsync(a => a.ImporteAplicado, ct);
    public async Task<IReadOnlyList<AutorizacionAnticipoCe>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => (await db.AutorizacionesPagoComercioExterior.AsNoTracking().Where(a => ids.Contains(a.FacturaProveedorId)).ToListAsync(ct))
            .Select(a => new AutorizacionAnticipoCe(a.FacturaProveedorId, PagosComercioExterior.Estado(a), a.Nivel1UsuarioId, a.Nivel1En, a.Nivel2UsuarioId, a.Nivel2En)).ToArray();
}

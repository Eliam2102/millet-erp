using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
public sealed class ComprobacionAnticipoCeReadAdapter(CuentasPorPagarDbContext db) : IComprobacionAnticipoCeReadPort
{
    public async Task<IReadOnlyList<string>> ErroresAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => await db.Set<ComprobacionAnticipoCe>().AsNoTracking().Where(x => ids.Contains(x.Id) && x.Error != null).Select(x => x.Error!).ToArrayAsync(ct);
}

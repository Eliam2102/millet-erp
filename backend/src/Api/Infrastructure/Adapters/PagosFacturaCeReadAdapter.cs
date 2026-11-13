using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.Tesoreria.Infrastructure.Persistence;
namespace Millet.Api.Infrastructure.Adapters;
public sealed class PagosFacturaCeReadAdapter(TesoreriaDbContext db) : IPagosFacturaCeReadPort
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> ImportesVigentesAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
        => await (from a in db.AplicacionesPagoProveedor.AsNoTracking()
            join m in db.MovimientosBancarios.AsNoTracking() on a.MovimientoId equals m.Id
            where m.EmpresaId == empresaId && ids.Contains(a.FacturaProveedorId) && !a.Revertida
            group a by a.FacturaProveedorId into pagos
            select new { Id = pagos.Key, Importe = pagos.Sum(a => a.ImporteAplicado) }).ToDictionaryAsync(x => x.Id, x => x.Importe, ct);
    public Task<DateOnly?> UltimoPagoAsync(Guid facturaId, CancellationToken ct)
        => (from a in db.AplicacionesPagoProveedor.AsNoTracking()
            join m in db.MovimientosBancarios.AsNoTracking() on a.MovimientoId equals m.Id
            where a.FacturaProveedorId == facturaId && !a.Revertida
            select (DateOnly?)m.FechaValor).MaxAsync(ct);
}

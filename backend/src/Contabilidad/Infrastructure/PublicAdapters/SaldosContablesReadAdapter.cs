using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
namespace Millet.Contabilidad.Infrastructure.PublicAdapters;
public sealed class SaldosContablesReadAdapter(ContabilidadDbContext db) : ISaldosContablesReadPort
{
    public async Task<IReadOnlyDictionary<Guid, decimal>> ObtenerAsync(DateOnly corte, IReadOnlyCollection<Guid> cuentas, IReadOnlyCollection<Guid>? sucursales, CancellationToken ct) =>
        await (from p in db.Polizas.AsNoTracking() join d in db.PolizasPartidas.AsNoTracking() on p.Id equals d.PolizaId
            where p.Fecha <= corte && p.Estado != EstadoPoliza.Borrador && cuentas.Contains(d.CuentaId)
                && (sucursales == null || sucursales.Contains(d.SucursalId))
            group d by d.CuentaId into g select new { Cuenta = g.Key, Saldo = g.Sum(x => x.Cargo - x.Abono) })
            .ToDictionaryAsync(x => x.Cuenta, x => x.Saldo, ct);
}

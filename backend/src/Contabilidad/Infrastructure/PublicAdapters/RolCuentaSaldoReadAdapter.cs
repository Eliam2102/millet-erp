using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>Adaptador de <see cref="IRolCuentaSaldoReadPort"/>: resuelve el rol como el motor y lee el saldo con <see cref="ISaldosContablesReadPort"/>.</summary>
public sealed class RolCuentaSaldoReadAdapter(ContabilidadDbContext db, ISaldosContablesReadPort saldos) : IRolCuentaSaldoReadPort
{
    public async Task<IReadOnlyDictionary<Guid, SaldoRolSucursal>> ObtenerAsync(string rol, DateOnly corte, IReadOnlyCollection<Guid> sucursales, CancellationToken ct)
    {
        var roles = await db.RolesCuenta.AsNoTracking().Where(r => r.Codigo == rol && r.DeletedAt == null).ToListAsync(ct);
        var cuentaPorSucursal = sucursales.Distinct().ToDictionary(s => s,
            s => GenerarPolizaDesdeEventoHandler.Rol(roles, rol, s, Poliza.MonedaNacional, corte)?.CuentaId);
        var ids = cuentaPorSucursal.Values.OfType<Guid>().Distinct().ToList();
        var cuentas = await db.Cuentas.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Codigo, c.Nombre }).ToDictionaryAsync(c => c.Id, ct);
        var resultado = new Dictionary<Guid, SaldoRolSucursal>();
        foreach (var (sucursal, cuentaId) in cuentaPorSucursal)
        {
            if (cuentaId is not { } id || !cuentas.TryGetValue(id, out var c))
            {
                resultado[sucursal] = new(null, null, null, null);
                continue;
            }
            var saldo = await saldos.ObtenerAsync(corte, [id], [sucursal], ct);
            resultado[sucursal] = new(id, c.Codigo, c.Nombre, saldo.GetValueOrDefault(id));
        }
        return resultado;
    }
}

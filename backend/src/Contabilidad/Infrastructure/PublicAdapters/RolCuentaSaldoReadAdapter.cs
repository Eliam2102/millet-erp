using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>Adaptador de <see cref="IRolCuentaSaldoReadPort"/>: resuelve el rol como el motor y lee el saldo con <see cref="ISaldosContablesReadPort"/>.</summary>
public sealed class RolCuentaSaldoReadAdapter(ContabilidadDbContext db, ISaldosContablesReadPort saldos) : IRolCuentaSaldoReadPort
{
    public async Task<IReadOnlyDictionary<(Guid SucursalId, string? Clasificador), SaldoRolSucursal>> ObtenerAsync(string rol, DateOnly corte,
        IReadOnlyCollection<(Guid SucursalId, string? Clasificador)> claves, IReadOnlyCollection<Guid>? sucursalesSaldo, CancellationToken ct)
    {
        var prefijo = rol + "_";
        var roles = await db.RolesCuenta.AsNoTracking().Where(r => (r.Codigo == rol || r.Codigo.StartsWith(prefijo)) && r.DeletedAt == null).ToListAsync(ct);
        var cuentaPorClave = claves.Distinct().ToDictionary(k => k,
            k => GenerarPolizaDesdeEventoHandler.CuentaPorClasificador(roles, rol, k.Clasificador, k.SucursalId, Poliza.MonedaNacional, corte));
        var ids = cuentaPorClave.Values.OfType<Guid>().Distinct().ToList();
        var cuentas = await db.Cuentas.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Codigo, c.Nombre }).ToDictionaryAsync(c => c.Id, ct);
        IReadOnlyDictionary<Guid, decimal> saldo = ids.Count == 0 ? new Dictionary<Guid, decimal>() : await saldos.ObtenerAsync(corte, ids, sucursalesSaldo, ct);
        return cuentaPorClave.ToDictionary(x => x.Key, x => x.Value is { } id && cuentas.TryGetValue(id, out var c)
            ? new SaldoRolSucursal(id, c.Codigo, c.Nombre, saldo.GetValueOrDefault(id))
            : new SaldoRolSucursal(null, null, null, null));
    }
}

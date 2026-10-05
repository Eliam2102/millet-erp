using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador de <see cref="ICuentaBancariaContabilidadPort"/> (F1-CON-02): las cuentas bancarias de Millet como dimensión
/// "banco" de una partida. Clave = número de cuenta; nombre = banco y moneda. El filtro global limita a la empresa del request.
/// </summary>
public sealed class CuentaBancariaContabilidadAdapter(TesoreriaDbContext db) : ICuentaBancariaContabilidadPort
{
    public async Task<IReadOnlyDictionary<Guid, AuxiliarContable>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<Guid, AuxiliarContable>();
        var arr = ids.Distinct().ToArray();
        return await db.CuentasBancarias.AsNoTracking().Where(c => arr.Contains(c.Id))
            .Select(c => new AuxiliarContable(c.Id, c.NumeroCuenta, c.Banco + " " + c.Moneda, c.Activa))
            .ToDictionaryAsync(a => a.Id, ct);
    }

    public async Task<IReadOnlyList<AuxiliarContable>> BuscarAsync(string? q, int limite, CancellationToken ct)
    {
        var patron = string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";
        return await db.CuentasBancarias.AsNoTracking()
            .Where(c => c.Activa)
            .Where(c => patron == null || EF.Functions.ILike(c.Banco, patron) || EF.Functions.ILike(c.NumeroCuenta, patron))
            .OrderBy(c => c.Banco).ThenBy(c => c.NumeroCuenta).Take(limite)
            .Select(c => new AuxiliarContable(c.Id, c.NumeroCuenta, c.Banco + " " + c.Moneda, true))
            .ToListAsync(ct);
    }
}

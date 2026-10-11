using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Reportes;

/// <summary>Fuente compartida del reporte de flujo y su serie diaria en Inicio.</summary>
public static class FlujoLectura
{
    public static IQueryable<CuentaBancaria> Cuentas(TesoreriaDbContext db, IReadOnlyList<Guid>? permitidas,
        string? moneda = null, Guid? cuentaId = null) => db.CuentasBancarias.AsNoTracking()
        .Where(c => permitidas == null || (permitidas ?? Array.Empty<Guid>()).Contains(c.Id))
        .Where(c => (cuentaId == null || c.Id == cuentaId) && (moneda == null || c.Moneda == moneda));

    public static IQueryable<MovimientoBancario> Movimientos(TesoreriaDbContext db, IReadOnlyList<Guid> cuentas) =>
        db.MovimientosBancarios.AsNoTracking().Where(m => cuentas.Contains(m.CuentaBancariaId));
}

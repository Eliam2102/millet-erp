using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Cierre;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;

/// <summary>
/// CON-12 (86e3jvvme): ¿ya se corrió la revaluación de cierre del mes (CXP-12)? Si no, cuántas facturas en moneda extranjera tenían
/// saldo al último día del mes (con el mismo saldo histórico del auxiliar de proveedores de CON-09). Solo lectura.
/// </summary>
public sealed class RevaluacionProveedoresCierreAdapter(CuentasPorPagarDbContext db, AuxiliarProveedoresContabilidadAdapter auxiliar)
    : IRevaluacionProveedoresCierreReadPort
{
    public async Task<EstadoRevaluacionProveedores> EstadoAsync(int anio, int mes, CancellationToken ct)
    {
        if (await db.RevaluacionesCierre.AsNoTracking().AnyAsync(r => r.Anio == anio && r.Mes == mes, ct))
            return new(true, 0);
        var cierre = new DateOnly(anio, mes, DateTime.DaysInMonth(anio, mes));
        var aux = await auxiliar.LeerAsync(cierre, null, ct);
        return new(false, aux.Documentos.Count(d => d.Moneda != "MXN"));
    }
}

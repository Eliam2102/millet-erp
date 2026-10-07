using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador productivo de <see cref="IPeriodoContableReadPort"/> (lo hospeda el dueño, ADR-0050). Orden de rechazo:
/// NoExiste → (periodo 13) Periodo13SoloAjusteAuditoria → Periodo13AntesDelCierreDeDiciembre → Cerrado.
/// El filtro global por empresa hace que un periodo de otra empresa no exista.
/// </summary>
public sealed class PeriodoContableReadAdapter(ContabilidadDbContext db) : IPeriodoContableReadPort
{
    public async Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct)
    {
        if (mes is < 1 or > 12) return false;
        return await db.Periodos.AsNoTracking()
            .AnyAsync(p => p.Ejercicio == año && p.Numero == mes && p.Estado == EstadoPeriodo.Abierto, ct);
    }

    public async Task<PeriodoValidacion> ValidarRegistroAsync(int ejercicio, int periodo, bool esManualAutorizada, CancellationToken ct)
    {
        var p = await db.Periodos.AsNoTracking().FirstOrDefaultAsync(x => x.Ejercicio == ejercicio && x.Numero == periodo, ct);
        if (p is null) return new(false, MotivoRechazoPeriodo.NoExiste);
        if (p.EsPeriodoAjustes)
        {
            if (!esManualAutorizada) return new(false, MotivoRechazoPeriodo.Periodo13SoloAjusteAuditoria);
            var diciembreCerrado = await db.Periodos.AsNoTracking()
                .AnyAsync(x => x.Ejercicio == ejercicio && x.Numero == 12 && x.Estado == EstadoPeriodo.Cerrado, ct);
            if (!diciembreCerrado) return new(false, MotivoRechazoPeriodo.Periodo13AntesDelCierreDeDiciembre);
        }
        return p.Abierto ? new(true, null) : new(false, MotivoRechazoPeriodo.Cerrado);
    }
}

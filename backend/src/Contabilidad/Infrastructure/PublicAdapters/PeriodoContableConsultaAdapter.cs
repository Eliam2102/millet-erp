using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Infrastructure.PublicAdapters;

/// <summary>Adaptador productivo de <see cref="IPeriodoContableConsultaPort"/>. El filtro global por empresa aplica.</summary>
public sealed class PeriodoContableConsultaAdapter(ContabilidadDbContext db) : IPeriodoContableConsultaPort
{
    public Task<EstadoPeriodoContable> ConsultarPorFechaAsync(DateOnly fecha, CancellationToken ct)
    {
        var (anio, numero) = PeriodoContable.PorFecha(fecha);
        return ConsultarAsync(anio, numero, ct);
    }

    public async Task<EstadoPeriodoContable> ConsultarAsync(int anio, int numero, CancellationToken ct)
    {
        var estado = await db.Periodos.AsNoTracking().Where(p => p.Anio == anio && p.Numero == numero)
            .Select(p => (EstadoPeriodo?)p.Estado).FirstOrDefaultAsync(ct);
        return new(anio, numero, estado ?? EstadoPeriodo.NoAbierto, estado is not null);
    }
}

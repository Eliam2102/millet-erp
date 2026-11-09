using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Cierre;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>CON-12 (86e3jvvme): ¿Almacén cerró el mes? Lo pregunta el checklist de cierre contable. Solo lectura.</summary>
public sealed class InventarioCierreContabilidadAdapter(AlmacenDbContext db, ICurrentEmpresaContext empresa) : IInventarioCierreReadPort
{
    public async Task<bool> MesCerradoAsync(int anio, int mes, CancellationToken ct)
    {
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(InventarioCierreContabilidadAdapter));
        return await db.Set<PeriodoCerrado>().AsNoTracking().AnyAsync(p => p.EmpresaId == empresaId && p.Anio == anio && p.Mes == mes, ct);
    }
}

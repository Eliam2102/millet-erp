using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;
namespace Millet.ActivosFijos.Infrastructure.PublicAdapters;
public sealed class ActivosFijosReadAdapter(ActivosFijosDbContext db) : IActivosFijosReadPort
{
    public async Task<ActivoFijoLectura?> ObtenerAsync(string activoRef, CancellationToken cancellationToken) =>
        await db.Activos.AsNoTracking().Where(x => x.Numero == activoRef && x.Estado == EstadoActivo.Activo)
            .Select(x => new ActivoFijoLectura(x.Numero, x.Descripcion, x.ValorActual - x.DepreciacionAcumulada, x.DepreciacionAcumulada, x.Importacion != TipoImportacionActivo.No))
            .SingleOrDefaultAsync(cancellationToken);
}

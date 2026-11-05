using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Application.PublicPorts;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class TransporteImportacionCeReadAdapter(ComercioExteriorDbContext db) : ITransporteImportacionCeReadPort
{
    public async Task<TransporteImportacionCeLectura?> ObtenerAsync(Guid ocId, CancellationToken ct)
    {
        var e = await db.Expedientes.AsNoTracking().Include(e => e.Contenedores).SingleOrDefaultAsync(e => e.OcId == ocId && e.Estado == EstadoExpediente.Abierto, ct);
        return e is null ? null : new(e.NumeroPedimentoVigenteImportacion, e.Contenedores.Where(c => c.Estado == EstadoContenedor.Activo && c.Numero != null)
            .OrderBy(c => c.Numero).Select(c => new ContenedorImportacionCeLectura(c.Numero!, c.BlGuia)).ToArray());
    }
}

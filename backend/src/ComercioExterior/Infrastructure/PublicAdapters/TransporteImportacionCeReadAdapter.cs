using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Compras.Application.PublicPorts;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class TransporteImportacionCeReadAdapter(ComercioExteriorDbContext db) : ITransporteImportacionCeReadPort
{
    public async Task<TransporteImportacionCeLectura?> ObtenerAsync(Guid ocId, CancellationToken ct)
    {
        var expedientes = await db.Expedientes.AsNoTracking().Include(e => e.Contenedores).Where(e => e.OcId == ocId && e.Estado == EstadoExpediente.Abierto).OrderByDescending(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync(ct);
        return expedientes.Count == 0 ? null : new(expedientes.Select(e => e.NumeroPedimentoVigenteImportacion).FirstOrDefault(p => p != null), expedientes.SelectMany(e => e.Contenedores).Where(c => c.Estado == EstadoContenedor.Activo && c.Numero != null)
            .OrderBy(c => c.Numero).Select(c => new ContenedorImportacionCeLectura(c.Numero!, c.BlGuia)).ToArray());
    }
}

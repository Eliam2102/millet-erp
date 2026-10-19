using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IAdjuntosEntidadReadPort"/> (Almacén). Lee el servicio genérico
/// de adjuntos (G1.2) con <c>AsNoTracking</c> y proyecta solo id y nombre: nunca el <c>BlobRef</c> (ADR-0058).
/// Reemplaza <c>NoOpAdjuntosEntidadReadPort</c> (PLATFORM-TODO &lt;AdjuntosEntidadReadAdapter&gt;).
/// </summary>
public sealed class AdjuntosEntidadReadAdapter(CompartidoDbContext db) : IAdjuntosEntidadReadPort
{
    public async Task<IReadOnlyList<AdjuntoEvidenciaLectura>> ListarVigentesAsync(
        string tipoEntidad, Guid entidadId, CancellationToken ct) =>
        await db.Adjuntos.AsNoTracking()
            .Where(a => a.TipoEntidad == tipoEntidad && a.EntidadId == entidadId && a.BajaEn == null)
            .OrderBy(a => a.NombreArchivo)
            .Select(a => new AdjuntoEvidenciaLectura(a.Id, a.NombreArchivo))
            .ToListAsync(ct);
}

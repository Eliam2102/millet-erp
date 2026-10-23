using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// ALM-04: resuelve el rack del sub-almacén de material en revisión (MAT-REV) del mismo almacén
/// que el sub-almacén disponible de la recepción. Lo dañado entra a ese rack real (la ÚNICA no admite entradas).
/// </summary>
internal static class UbicacionRevisionResolver
{
    public static async Task<(Guid SubAlmacenRevisionId, Guid UbicacionRevisionId)> ResolverAsync(
        AlmacenDbContext db, Guid subAlmacenDisponibleId, CancellationToken ct)
    {
        var almacenId = await db.SubAlmacenes.AsNoTracking()
            .Where(s => s.Id == subAlmacenDisponibleId)
            .Select(s => (Guid?)s.AlmacenId)
            .FirstOrDefaultAsync(ct);

        var subRevisionId = almacenId is null ? null : await db.SubAlmacenes.AsNoTracking()
            .Where(s => s.AlmacenId == almacenId
                     && s.Tipo == TipoSubAlmacen.MaterialEnRevision
                     && s.Estatus == EstatusCatalogo.Activo)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

        var ubicacionId = subRevisionId is null ? null : await db.Ubicaciones.AsNoTracking()
            .Where(u => u.SubAlmacenId == subRevisionId && !u.EsDefault && u.Estatus == EstatusCatalogo.Activo)
            .OrderBy(u => u.Clave)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        if (subRevisionId is null || ubicacionId is null)
            throw new BusinessRuleException(
                "RECEPCION_SIN_UBICACION_REVISION",
                "El almacén no tiene una ubicación activa de material en revisión: da de alta una ubicación (rack) en el sub-almacén MAT-REV antes de registrar cantidades dañadas.");

        return (subRevisionId.Value, ubicacionId.Value);
    }
}

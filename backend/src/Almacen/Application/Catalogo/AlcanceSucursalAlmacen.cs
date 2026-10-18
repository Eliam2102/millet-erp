using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Catalogo;

/// <summary>
/// Alcance por sucursal (ADR-0051) de los sub-almacenes de Almacén. Un sub-almacén
/// pertenece a la sucursal de su almacén padre; las consultas agregadas (saldos,
/// reportes) y las verificaciones por id usan estas funciones para no mezclar sucursales.
/// </summary>
internal static class AlcanceSucursalAlmacen
{
    /// <summary>Ids de los sub-almacenes cuyo almacén padre pertenece a alguna de las sucursales dadas.</summary>
    public static IQueryable<Guid> SubAlmacenesPermitidos(AlmacenDbContext db, IReadOnlyList<Guid> sucursales) =>
        from s in db.SubAlmacenes
        join a in db.Almacenes on s.AlmacenId equals a.Id
        where sucursales.Contains(a.SucursalId)
        select s.Id;

    /// <summary>
    /// Lanza <see cref="ForbiddenException"/> si el sub-almacén no pertenece a las sucursales permitidas.
    /// <c>sucursales = null</c> (alcance corporativo) o <c>subAlmacenId = null</c> no verifican nada.
    /// </summary>
    public static async Task VerificarSubAlmacenAsync(AlmacenDbContext db, Guid? subAlmacenId,
        IReadOnlyList<Guid>? sucursales, CancellationToken ct)
    {
        if (sucursales is null || subAlmacenId is null) return;
        var id = subAlmacenId.Value;
        var permitido = await SubAlmacenesPermitidos(db, sucursales).AnyAsync(x => x == id, ct);
        if (!permitido)
            throw new ForbiddenException("SUCURSAL_NO_ASOCIADA", "No tienes acceso a los datos de esta sucursal.");
    }
}

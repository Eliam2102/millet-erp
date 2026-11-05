using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Catalogo;

/// <summary>
/// F5 (86e3pc50v): un documento dirigido a una sucursal no se opera en un almacén de otra; aplica aunque el usuario tenga bypass.
/// </summary>
internal static class SucursalDestinoGuard
{
    public static void Validar(Guid? sucursalDocumento, Guid? sucursalAlmacen, string codigo, string mensaje)
    {
        if (sucursalDocumento is Guid d && sucursalAlmacen is Guid a && d != a)
            throw new BusinessRuleException(codigo, mensaje);
    }

    /// <summary>
    /// F-RQOC-06: valida la sucursal de las ubicaciones antes que su asignación, para no pedir que se asigne
    /// el artículo en un almacén de otra sucursal.
    /// </summary>
    public static async Task ValidarUbicacionesAsync(AlmacenDbContext db, Guid? sucursalDocumento,
        IEnumerable<Guid?> ubicaciones, string codigo, string mensaje, CancellationToken ct)
    {
        if (sucursalDocumento is not Guid destino) return;
        var ids = ubicaciones.OfType<Guid>().Distinct().ToList();
        var otraSucursal = await db.Ubicaciones.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Join(db.SubAlmacenes, u => u.SubAlmacenId, s => s.Id, (u, s) => s.AlmacenId)
            .Join(db.Almacenes, almacenId => almacenId, a => a.Id, (almacenId, a) => a.SucursalId)
            .AnyAsync(sucursal => sucursal != destino, ct);
        if (otraSucursal) throw new BusinessRuleException(codigo, mensaje);
    }
}

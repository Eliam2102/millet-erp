using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Sucursal que nombra la app de Almacenes: por su clave del ERP o por cualquiera de sus claves de A+W. <c>ClaveAw</c> admite
/// alias separados por coma (en QA Conkal es <c>CONKAL,PERIFERICO,OBRAS</c>, ver <see cref="Sucursal.ClavesAw"/>), así que no se
/// compara la columna completa.
/// </summary>
internal static class SucursalPorClaveApp
{
    public static async Task<(Guid Id, string Clave)?> BuscarAsync(CompartidoDbContext db, string codigo, CancellationToken ct)
    {
        var c = codigo.Trim();
        var cu = c.ToUpperInvariant();
        var candidatas = await db.Sucursales.AsNoTracking()
            .Where(x => x.Clave == c || (x.ClaveAw != null && EF.Functions.ILike(x.ClaveAw, "%" + c + "%")))
            .Select(x => new { x.Id, x.Clave, x.ClaveAw }).ToListAsync(ct);
        var s = candidatas.FirstOrDefault(x => x.Clave == c) ?? candidatas.FirstOrDefault(x => Sucursal.ClavesAw(x.ClaveAw).Contains(cu));
        return s is null ? null : (s.Id, s.Clave);
    }
}

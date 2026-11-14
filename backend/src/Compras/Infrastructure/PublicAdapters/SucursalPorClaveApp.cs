using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Sucursal que nombra la app de Almacenes: por su clave del ERP o por cualquiera de sus claves de A+W. <c>ClaveAw</c> admite
/// alias separados por coma (en QA Conkal es <c>CONKAL,PERIFERICO,OBRAS</c>, ver <see cref="Sucursal.ClavesAw"/>), así que no se
/// compara la columna completa. Las claves no distinguen mayúsculas. Si ninguna clave coincide, se acepta el NOMBRE exacto de la
/// sucursal solo cuando es único (cargas iniciales que traen «CONKAL» como nombre); una clave siempre gana sobre un nombre.
/// </summary>
internal static class SucursalPorClaveApp
{
    internal sealed record Candidata(Guid Id, string Clave, string? ClaveAw, string Nombre);

    /// <summary>Resultado de la búsqueda: encontrada (<see cref="Id"/> ≠ vacío), no existe o ambigua (varias sucursales con ese nombre).</summary>
    internal sealed record Resultado(Guid Id, string Clave, bool Ambigua)
    {
        public bool Encontrada => Id != Guid.Empty;

        public string Mensaje(string codigo) => Ambigua
            ? $"El nombre '{codigo.Trim()}' corresponde a más de una sucursal; use su clave del ERP o su clave A+W."
            : $"No existe la sucursal '{codigo.Trim()}'.";
    }

    public static async Task<Resultado> BuscarAsync(CompartidoDbContext db, string codigo, CancellationToken ct)
    {
        var c = codigo.Trim();
        var candidatas = await db.Sucursales.AsNoTracking()
            .Where(x => EF.Functions.ILike(x.Clave, c) || (x.ClaveAw != null && EF.Functions.ILike(x.ClaveAw, "%" + c + "%"))
                || EF.Functions.ILike(x.Nombre, c))
            .Select(x => new Candidata(x.Id, x.Clave, x.ClaveAw, x.Nombre)).ToListAsync(ct);
        return Elegir(candidatas, c);
    }

    /// <summary>Elección entre las candidatas (el filtro de la base es amplio; aquí se decide con comparación exacta).</summary>
    internal static Resultado Elegir(IReadOnlyList<Candidata> candidatas, string codigo)
    {
        var c = codigo.Trim();
        var cu = c.ToUpperInvariant();
        var s = candidatas.FirstOrDefault(x => string.Equals(x.Clave, c, StringComparison.Ordinal))
            ?? candidatas.FirstOrDefault(x => string.Equals(x.Clave, c, StringComparison.OrdinalIgnoreCase))
            ?? candidatas.FirstOrDefault(x => Sucursal.ClavesAw(x.ClaveAw).Contains(cu));
        if (s is null)
        {
            var porNombre = candidatas.Where(x => string.Equals(x.Nombre.Trim(), c, StringComparison.OrdinalIgnoreCase)).ToList();
            if (porNombre.Count > 1) return new(Guid.Empty, "", true);
            s = porNombre.Count == 1 ? porNombre[0] : null;
        }
        return s is null ? new(Guid.Empty, "", false) : new(s.Id, s.Clave, false);
    }
}

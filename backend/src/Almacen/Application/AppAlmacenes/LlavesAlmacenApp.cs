using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>Almacén de un renglón de la app: <c>almacenGeneral.tipo</c> + <c>almacenGeneral.linea</c> (vacía en herraje), normalizados.</summary>
public sealed record LlaveAlmacenApp
{
    public string Tipo { get; }
    public string Linea { get; }

    public LlaveAlmacenApp(string tipo, string? linea)
    {
        Tipo = AlmacenApp.Normalizar(tipo);
        Linea = AlmacenApp.Normalizar(linea);
    }

    public override string ToString() => Linea.Length == 0 ? $"{Tipo} (sin línea)" : $"{Tipo} {Linea}";
}

/// <summary>Ubicación del ERP a la que resuelve una llave.</summary>
public sealed record UbicacionApp(Guid UbicacionId, Guid SubAlmacenId, Guid AlmacenId);

/// <summary>
/// Resuelve el almacén de cada renglón de un mensaje por <c>sucursal + tipo + línea</c> (M-446). Mapa null = comportamiento
/// previo (ningún renglón trae tipo, o la sucursal aún no tiene llaves: solo Conkal está habilitada). Con llaves en la
/// sucursal, cada renglón debe traer la suya y existir; si no, error (el llamador acusa ERROR con folio de revisión).
/// </summary>
public static class LlavesAlmacenApp
{
    public static async Task<(IReadOnlyDictionary<LlaveAlmacenApp, UbicacionApp>? Mapa, string? Error)> ResolverAsync(
        AlmacenDbContext db, Guid sucursalId, IReadOnlyCollection<LlaveAlmacenApp?> llaves, CancellationToken ct)
    {
        if (llaves.All(l => l is null)) return (null, null);
        var catalogo = await (from a in db.AlmacenesApp.AsNoTracking()
                              join u in db.Ubicaciones.AsNoTracking() on a.UbicacionId equals u.Id
                              join s in db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals s.Id
                              where a.SucursalId == sucursalId && a.Activo && u.Estatus == EstatusCatalogo.Activo
                              select new { a.TipoAlmacen, a.Linea, u.Id, u.SubAlmacenId, s.AlmacenId }).ToListAsync(ct);
        if (catalogo.Count == 0) return (null, null);
        if (llaves.Any(l => l is null))
            return (null, "Falta almacenGeneral.tipo en un renglón: en esta sucursal cada renglón dice su almacén (tipo + línea).");

        var mapa = new Dictionary<LlaveAlmacenApp, UbicacionApp>();
        foreach (var llave in llaves.Distinct())
        {
            var c = catalogo.FirstOrDefault(x => x.TipoAlmacen == llave!.Tipo && x.Linea == llave.Linea);
            if (c is null)
                return (null, $"El almacén {llave} no está en la lista de almacenes del ERP para esta sucursal.");
            mapa[llave!] = new UbicacionApp(c.Id, c.SubAlmacenId, c.AlmacenId);
        }
        return (mapa, null);
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>Partida de ENTRADA_ALMACEN ya resuelta a ids del ERP (cantidad en la unidad del saldo: m² en vidrio).</summary>
public sealed record EntradaApp(
    Guid ArticuloId, string UnidadMedida, Guid? ProductoAwVarianteId, decimal Cantidad, decimal? AnchoMm = null, decimal? AltoMm = null);

/// <summary>
/// ENTRADA_ALMACEN de la app (M-159, contrato v8): la app ya acomodó las hojas en sus racks; el ERP solo sube existencia por
/// sucursal + artículo A+W + medida, sin pedir rack, en el almacén general (ubicación ÚNICA) de la sucursal. No guarda: el
/// llamador confirma junto con el <c>MensajeApp</c>.
/// </summary>
public static class EntradaAppAlmacenes
{
    public static async Task<(string Estado, string? Folio, string? Error)> RegistrarAsync(
        AlmacenDbContext db, Guid empresaId, Guid usuarioId, Guid sucursalId, string motivo,
        IReadOnlyList<EntradaApp> entradas, CancellationToken ct)
    {
        // ponytail: un solo almacén general por sucursal (primero de materiales directos). El mapeo por tipo + línea
        // (EXPORTACION/OBRAS/GENERAL/IMPORTACION TEMPORAL × HEGLA 1/HEGLA 2/BYSTRONIC) llega con el catálogo de 16 llaves.
        var general = await (from u in db.Ubicaciones
                             join s in db.SubAlmacenes on u.SubAlmacenId equals s.Id
                             join a in db.Almacenes on s.AlmacenId equals a.Id
                             where a.SucursalId == sucursalId && u.EsDefault && u.Estatus == EstatusCatalogo.Activo
                                   && s.Estatus == EstatusCatalogo.Activo && a.Estatus == EstatusCatalogo.Activo
                                   && s.Tipo != TipoSubAlmacen.MaterialEnRevision && s.Tipo != TipoSubAlmacen.Transitorio
                             orderby (s.Tipo == TipoSubAlmacen.MaterialesDirectos ? 0 : 1), a.Clave, s.Clave
                             select (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (general is not Guid ubicacionId)
            return ("ERROR", null, "La sucursal no tiene un almacén general activo donde registrar la entrada.");

        var lineas = new List<LineaRevisionMov>(entradas.Count);
        foreach (var e in entradas)
        {
            // PLATFORM-TODO(<M-159-oc-link>): la entrada aún no se liga a la OC (datos.compra) ni publica
            // OcRecepcionRegistradaEvent; el costo de la línea de la OC llega con M-447. Mientras, entra al promedio vigente
            // de esa medida en el almacén general (0 si no hay saldo), así no mueve el promedio.
            var costo = await db.SaldosInventario.AsNoTracking()
                .Where(s => s.UbicacionId == ubicacionId && s.ArticuloId == e.ArticuloId && s.ProductoAwVarianteId == e.ProductoAwVarianteId)
                .Select(s => (decimal?)s.CostoPromedioMxn).FirstOrDefaultAsync(ct) ?? 0m;
            lineas.Add(new LineaRevisionMov(e.ArticuloId, e.UnidadMedida, e.Cantidad, costo, ubicacionId,
                e.ProductoAwVarianteId, e.AnchoMm, e.AltoMm));
        }
        var mov = await MovimientosRevision.AgregarAsync(db, empresaId, usuarioId, TipoMovimiento.EntradaCompra,
            DateOnly.FromDateTime(DateTime.UtcNow), motivo, lineas, ct);
        return (SalidaAppAlmacenes.Recibido, mov.Folio, null);
    }
}

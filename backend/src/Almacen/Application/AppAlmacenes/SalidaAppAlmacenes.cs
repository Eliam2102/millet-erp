using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>Salida informada por la app AlmacenRQ_MP, ya resuelta a ids del ERP.</summary>
public sealed record SalidaApp(
    TipoMovimiento Tipo, Guid SucursalId, Guid ArticuloId, string UnidadMedida, Guid? ProductoAwVarianteId,
    decimal Cantidad, decimal? CostoUnitarioMxn, string Motivo, decimal? AnchoMm = null, decimal? AltoMm = null);

/// <summary>
/// Salidas de la app de Almacenes (F1-MP-07; ACUERDO 28-oct reglas 1–4, correo de Jorge 30-oct punto 4):
/// baja existencia del artículo A+W + medida en los almacenes de la sucursal, a costo promedio salvo que el
/// mensaje traiga el suyo. Sin existencia suficiente NO es error: el mensaje se acepta y queda
/// <see cref="EnRevision"/> sin mover saldo (bandeja de revisión). No guarda: el llamador hace un solo
/// <c>SaveChangesAsync</c> junto con el <c>MensajeApp</c> (idempotencia por mensajeId) y confirma la transacción.
/// </summary>
public static class SalidaAppAlmacenes
{
    public const string Recibido = "RECIBIDO";
    public const string EnRevision = "EN_REVISION";

    public static async Task<(string Estado, string? Folio, string? Error)> RegistrarAsync(
        AlmacenDbContext db, Guid empresaId, Guid usuarioId, SalidaApp s, CancellationToken ct)
    {
        var saldosQ = from saldo in db.SaldosInventario
                      join sub in db.SubAlmacenes on saldo.SubAlmacenId equals sub.Id
                      join alm in db.Almacenes on sub.AlmacenId equals alm.Id
                      where alm.SucursalId == s.SucursalId && saldo.ArticuloId == s.ArticuloId
                            && saldo.ProductoAwVarianteId == s.ProductoAwVarianteId
                            && sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                            && saldo.CantidadDisponible > 0
                      select saldo;

        if (db.Database.IsNpgsql())
        {
            // Mismo orden de bloqueo que la liberación de revisión: evita que dos salidas concurrentes
            // pasen la validación con la misma existencia (el CHECK de saldo no negativo es la última red).
            var ubicaciones = await saldosQ.Select(x => x.UbicacionId).Distinct().ToArrayAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM almacen.saldos_inventario WHERE articulo_id = {s.ArticuloId} AND ubicacion_id = ANY({ubicaciones}) ORDER BY articulo_id, ubicacion_id, producto_aw_variante_id, lote FOR UPDATE", ct);
        }

        var saldos = await saldosQ.OrderByDescending(x => x.CantidadDisponible).ThenBy(x => x.UbicacionId).ToListAsync(ct);
        var disponible = saldos.Sum(x => x.CantidadDisponible);
        if (disponible < s.Cantidad)
            return (EnRevision, null, $"Sin existencia suficiente: disponible {disponible:0.####}, solicitada {s.Cantidad:0.####}.");

        var lineas = new List<(Guid SubAlmacenId, LineaRevisionMov Linea)>();
        var resta = s.Cantidad;
        foreach (var saldo in saldos)
        {
            if (resta <= 0) break;
            var toma = Math.Min(resta, saldo.CantidadDisponible);
            lineas.Add((saldo.SubAlmacenId, new LineaRevisionMov(s.ArticuloId, s.UnidadMedida, toma,
                s.CostoUnitarioMxn ?? saldo.CostoPromedioMxn, saldo.UbicacionId,
                s.ProductoAwVarianteId, s.AnchoMm, s.AltoMm, saldo.Lote)));
            resta -= toma;
        }

        // Un movimiento solo lleva líneas de un sub-almacén: uno por sub-almacén tocado.
        var folios = new List<string>();
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var grupo in lineas.GroupBy(l => l.SubAlmacenId))
        {
            var mov = await MovimientosRevision.AgregarAsync(db, empresaId, usuarioId, s.Tipo, fecha, s.Motivo,
                grupo.Select(l => l.Linea).ToList(), ct);
            folios.Add(mov.Folio);
        }
        return (Recibido, string.Join(", ", folios), null);
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>
/// Salida informada por la app AlmacenRQ_MP, ya resuelta a ids del ERP. <c>CentroCostoId</c> (H1, 86e3z5w25): el equipo (Dim3) o, si no
/// se capturó, el área (Dim2) del movimiento; va en cada línea y en el evento contable, y el motor deriva de él la ubicación.
/// </summary>
public sealed record SalidaApp(
    TipoMovimiento Tipo, Guid SucursalId, Guid ArticuloId, string UnidadMedida, Guid? ProductoAwVarianteId,
    decimal Cantidad, decimal? CostoUnitarioMxn, string Motivo, decimal? AnchoMm = null, decimal? AltoMm = null,
    string? Causa = null, string? ArticuloAw = null, Guid? CentroCostoId = null);

/// <summary>
/// Salidas de la app de Almacenes (F1-MP-07; ACUERDO 28-oct reglas 1–4, correos de Jorge 30-oct y 5-nov):
/// baja existencia del artículo A+W + medida en los almacenes de la sucursal, a costo promedio salvo que el
/// mensaje traiga el suyo (promedio de A+W). Sin existencia suficiente (5-nov) no se acepta en silencio: el mensaje
/// queda <see cref="EnRevision"/> sin mover saldo (bandeja de revisión) y el llamador lo acusa ERROR con
/// <see cref="FolioRevision"/> y el motivo, para que sus siguientes de la misma referencia esperen. No guarda: el llamador hace un solo
/// <c>SaveChangesAsync</c> junto con el <c>MensajeApp</c> (idempotencia por mensajeId) y confirma la transacción.
/// </summary>
public static class SalidaAppAlmacenes
{
    public const string Recibido = "RECIBIDO";
    public const string EnRevision = "EN_REVISION";

    /// <summary>Folio del ERP para un mensaje marcado en la bandeja (sin movimiento): determinista, así el DUPLICADO lo repite.</summary>
    public static string FolioRevision(string mensajeId) => $"REV-{mensajeId}";

    public static Task<(string Estado, string? Folio, string? Error)> RegistrarAsync(
        AlmacenDbContext db, IIntegrationEventPublisher events, Guid empresaId, Guid usuarioId, SalidaApp s, CancellationToken ct) =>
        RegistrarAsync(db, events, empresaId, usuarioId, [s], ct);

    /// <summary>
    /// Varias salidas del mismo mensaje, todo o nada: si la existencia de cualquier (artículo, variante) no alcanza
    /// para la suma de sus partidas, el mensaje queda <see cref="EnRevision"/> y no se crea ningún movimiento.
    /// Todas las partidas comparten sucursal (la del primer elemento).
    /// </summary>
    public static async Task<(string Estado, string? Folio, string? Error)> RegistrarAsync(
        AlmacenDbContext db, IIntegrationEventPublisher events, Guid empresaId, Guid usuarioId,
        IReadOnlyList<SalidaApp> salidas, CancellationToken ct)
    {
        var sucursalId = salidas[0].SucursalId;
        var porClave = salidas.GroupBy(x => (x.ArticuloId, x.ProductoAwVarianteId))
            .OrderBy(g => g.Key.ArticuloId).ToList();

        // Fase 1: carga/bloquea saldos por clave y valida la suma; el pendiente por saldo se lleva localmente.
        var restante = new Dictionary<(Guid, Guid?), List<(SaldoInventario Saldo, decimal Resta)>>();
        foreach (var grp in porClave)
        {
            var (articuloId, varianteId) = grp.Key;
            var saldosQ = from saldo in db.SaldosInventario
                          join sub in db.SubAlmacenes on saldo.SubAlmacenId equals sub.Id
                          join alm in db.Almacenes on sub.AlmacenId equals alm.Id
                          where alm.SucursalId == sucursalId && saldo.ArticuloId == articuloId
                                && saldo.ProductoAwVarianteId == varianteId
                                && sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                                && saldo.CantidadDisponible > 0
                          select saldo;

            if (db.Database.IsNpgsql())
            {
                // Mismo orden de bloqueo que la liberación de revisión: evita que dos salidas concurrentes
                // pasen la validación con la misma existencia (el CHECK de saldo no negativo es la última red).
                var ubicaciones = await saldosQ.Select(x => x.UbicacionId).Distinct().ToArrayAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM almacen.saldos_inventario WHERE articulo_id = {articuloId} AND ubicacion_id = ANY({ubicaciones}) ORDER BY articulo_id, ubicacion_id, producto_aw_variante_id, lote FOR UPDATE", ct);
            }

            var saldos = await saldosQ.OrderByDescending(x => x.CantidadDisponible).ThenBy(x => x.UbicacionId).ToListAsync(ct);
            var disponible = saldos.Sum(x => x.CantidadDisponible);
            var pedida = grp.Sum(x => x.Cantidad);
            if (disponible < pedida)
            {
                var m = grp.First();
                var medida = m.AnchoMm is null ? "" : $" {m.AnchoMm:0.####}x{m.AltoMm:0.####}";
                return (EnRevision, null,
                    $"Sin existencia suficiente (artículo A+W {m.ArticuloAw ?? m.ArticuloId.ToString()}{medida} mm): disponible {disponible:0.####} {m.UnidadMedida}, solicitada {pedida:0.####} {m.UnidadMedida}.");
            }
            restante[(articuloId, varianteId)] = saldos.Select(x => (x, x.CantidadDisponible)).ToList();
        }

        // Fase 2: reparte cada partida entre los saldos de su clave, descontando lo ya tomado por partidas previas.
        var lineas = new List<(TipoMovimiento Tipo, string Motivo, Guid SubAlmacenId, string Causa, LineaRevisionMov Linea)>();
        foreach (var s in salidas)
        {
            var lista = restante[(s.ArticuloId, s.ProductoAwVarianteId)];
            var resta = s.Cantidad;
            for (var i = 0; i < lista.Count && resta > 0; i++)
            {
                var (saldo, libre) = lista[i];
                if (libre <= 0) continue;
                var toma = Math.Min(resta, libre);
                lineas.Add((s.Tipo, s.Motivo, saldo.SubAlmacenId, s.Causa ?? SalidaInventarioRegistradaIntegrationEvent.CausaPorDefecto(s.Tipo), new LineaRevisionMov(s.ArticuloId, s.UnidadMedida, toma,
                    s.CostoUnitarioMxn ?? saldo.CostoPromedioMxn, saldo.UbicacionId,
                    s.ProductoAwVarianteId, s.AnchoMm, s.AltoMm, saldo.Lote, s.CentroCostoId)));
                lista[i] = (saldo, libre - toma);
                resta -= toma;
            }
        }

        // Un movimiento solo lleva líneas de un sub-almacén: uno por (tipo, motivo, sub-almacén) tocado.
        var folios = new List<string>();
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var grupo in lineas.GroupBy(l => (l.Tipo, l.Motivo, l.SubAlmacenId)))
        {
            var mov = await MovimientosRevision.AgregarAsync(db, empresaId, usuarioId, grupo.Key.Tipo, fecha, grupo.Key.Motivo,
                grupo.Select(l => l.Linea).ToList(), ct);
            await SalidaInventarioRegistradaIntegrationEvent.PublicarAsync(
                events, db, empresaId, mov.Id, grupo.Key.SubAlmacenId, grupo.First().Causa, ct);
            folios.Add(mov.Folio);
        }
        return (Recibido, string.Join(", ", folios), null);
    }
}

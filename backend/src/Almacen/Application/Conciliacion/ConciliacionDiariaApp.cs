using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conciliacion;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Conciliacion;

/// <summary>Físico que reporta A+W o el conteo por hoja/medida/lote: hojas enteras si hay medida, unidad base si no.</summary>
public sealed record FisicoLinea(Guid ArticuloId, Guid? ProductoAwVarianteId, string? Lote, decimal Fisico);

/// <summary>
/// A4.6: concilia la existencia teórica del ERP de un almacén contra el físico reportado, sin tolerancia. Faltante = salida no
/// planificada (movimiento SNP que baja el saldo a lo físico, con el evento de salida para Contabilidad); sobrante = diferencia
/// sin movimiento. Solo compara las líneas reportadas. Rehacer la corrida con el mismo físico no repite la salida: el saldo ya
/// cuadra. No guarda ni confirma: el llamador abre la transacción antes y hace un solo <c>SaveChangesAsync</c> + commit.
/// </summary>
public static class ConciliacionDiariaApp
{
    public static async Task<(ConciliacionDiaria Corrida, IReadOnlyList<DiferenciaConciliacion> Diferencias)> EjecutarAsync(
        AlmacenDbContext db, IIntegrationEventPublisher events, IVarianteVidrioReadPort variantes, IArticuloReadPort articulos,
        IPeriodoContableReadPort periodo, Guid empresaId, Guid usuarioId, Guid almacenId, DateOnly? fechaSolicitada, string fuente,
        IReadOnlyList<FisicoLinea> lineas, CancellationToken ct)
    {
        if (lineas.Count == 0)
            throw new BusinessRuleException("CONCILIACION_SIN_LINEAS", "Reporta al menos una línea de físico.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("CONCILIACION_SIN_USUARIO", "Se requiere usuario autenticado.");
        if (lineas.Any(l => l.Fisico < 0))
            throw new BusinessRuleException("CONCILIACION_FISICO_NEGATIVO", "El físico no puede ser negativo.");
        var hoy = FechaNegocio.Hoy(DateTimeOffset.UtcNow);
        var fecha = fechaSolicitada ?? hoy;
        if (fecha > hoy)
            throw new BusinessRuleException("CONCILIACION_FECHA_FUTURA", "No se concilia un día que no ha ocurrido.");
        var llaves = lineas.Select(l => (l.ArticuloId, l.ProductoAwVarianteId, Lote: LineaMovimiento.NormalizarLote(l.Lote))).ToList();
        if (llaves.Distinct().Count() != llaves.Count)
            throw new BusinessRuleException("CONCILIACION_LINEA_DUPLICADA", "Hay dos líneas del mismo artículo, medida y lote.");
        var almacen = await db.Almacenes.FirstOrDefaultAsync(a => a.Id == almacenId, ct)
            ?? throw new EntityNotFoundException("ALMACEN_NO_ENCONTRADO", $"No existe el almacén '{almacenId}'.");

        var ids = lineas.Select(l => l.ArticuloId).Distinct().ToArray();
        var maestro = await articulos.ObtenerPorIdsAsync(ids, ct);
        var medidas = await variantes.ObtenerPorIdsAsync(
            lineas.Where(l => l.ProductoAwVarianteId != null).Select(l => l.ProductoAwVarianteId!.Value).Distinct().ToArray(), ct);

        var saldosQ = from s in db.SaldosInventario
                      join sub in db.SubAlmacenes on s.SubAlmacenId equals sub.Id
                      where sub.AlmacenId == almacenId && ids.Contains(s.ArticuloId)
                          && sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                      select s;
        if (db.Database.IsNpgsql())
        {
            // Mismo orden de bloqueo que las salidas de la app: dos corridas o una salida concurrente no leen la misma existencia.
            var ubicaciones = await saldosQ.Select(x => x.UbicacionId).Distinct().ToArrayAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM almacen.saldos_inventario WHERE articulo_id = ANY({ids}) AND ubicacion_id = ANY({ubicaciones}) ORDER BY articulo_id, ubicacion_id, producto_aw_variante_id, lote FOR UPDATE", ct);
        }
        var saldos = await saldosQ.ToListAsync(ct);

        var difs = new List<DiferenciaConciliacion>();
        var porSubalmacen = new Dictionary<Guid, List<LineaRevisionMov>>();
        var pendientes = new List<(Guid ArticuloId, Guid? Variante, string? Lote, decimal? Area, decimal Teorico, decimal Fisico,
            decimal Costo, decimal Valor, List<Guid> Subs)>();
        foreach (var l in lineas.OrderBy(x => x.ArticuloId))
        {
            var lote = LineaMovimiento.NormalizarLote(l.Lote);
            decimal? area = null;
            if (l.ProductoAwVarianteId is Guid v)
            {
                if (l.Fisico != decimal.Truncate(l.Fisico))
                    throw new BusinessRuleException("HOJAS_ENTERAS", "Las hojas se cuentan en números enteros.");
                area = medidas.GetValueOrDefault(v)?.AreaHojaM2
                    ?? throw new BusinessRuleException("VARIANTE_SIN_MEDIDA", "La medida de la línea no tiene ancho y alto en A+W; no se puede conciliar en hojas.");
            }
            var filas = saldos.Where(s => s.ArticuloId == l.ArticuloId && s.ProductoAwVarianteId == l.ProductoAwVarianteId && s.Lote == lote)
                .OrderByDescending(s => s.CantidadDisponible).ThenBy(s => s.UbicacionId).ToList();
            var teoricoBase = filas.Sum(s => s.CantidadDisponible);
            var teorico = area is { } a ? Math.Round(teoricoBase / a, 4) : teoricoBase;
            if (teorico == l.Fisico) continue;

            decimal costo = 0, valor = 0;
            var subs = new List<Guid>();
            if (l.Fisico < teorico)
            {
                var resta = Math.Round((teorico - l.Fisico) * (area ?? 1m), 4);
                resta = Math.Min(resta, teoricoBase);
                var um = maestro.GetValueOrDefault(l.ArticuloId)?.UnidadMedida ?? (area is null ? "PZA" : "M2");
                var ancho = l.ProductoAwVarianteId is Guid v1 ? medidas.GetValueOrDefault(v1)?.AnchoMm : null;
                var alto = l.ProductoAwVarianteId is Guid v2 ? medidas.GetValueOrDefault(v2)?.AltoMm : null;
                var total = resta;
                foreach (var s in filas.Where(x => x.CantidadDisponible > 0))
                {
                    if (resta <= 0) break;
                    var toma = Math.Min(resta, s.CantidadDisponible);
                    valor += toma * s.CostoPromedioMxn;
                    if (!porSubalmacen.TryGetValue(s.SubAlmacenId, out var lst)) porSubalmacen[s.SubAlmacenId] = lst = [];
                    lst.Add(new LineaRevisionMov(l.ArticuloId, um, toma, s.CostoPromedioMxn, s.UbicacionId,
                        l.ProductoAwVarianteId, ancho, alto, s.Lote));
                    if (!subs.Contains(s.SubAlmacenId)) subs.Add(s.SubAlmacenId);
                    resta -= toma;
                }
                costo = total > 0 ? Math.Round(valor / total, 4) : 0;
                valor = Math.Round(valor, 2);
            }
            pendientes.Add((l.ArticuloId, l.ProductoAwVarianteId, lote, area, teorico, l.Fisico, costo, valor, subs));
        }

        if (porSubalmacen.Count > 0) await PeriodoCerradoValidator.LanzarSiCerradoAsync(db, empresaId, fecha, periodo, ct);

        // Un movimiento solo lleva líneas de un sub-almacén: uno SNP por sub-almacén tocado.
        var motivo = $"Salida no planificada: conciliación diaria del {fecha:yyyy-MM-dd}";
        var foliosPorSub = new Dictionary<Guid, MovimientoCreado>();
        foreach (var (subId, lst) in porSubalmacen.OrderBy(x => x.Key))
        {
            var mov = await MovimientosRevision.AgregarAsync(db, empresaId, usuarioId, TipoMovimiento.SalidaNoPlanificada, fecha,
                motivo, lst, ct);
            await SalidaInventarioRegistradaIntegrationEvent.PublicarAsync(events, db, empresaId, mov.Id, subId, "salida_no_planificada", ct);
            foliosPorSub[subId] = mov;
        }

        var corrida = new ConciliacionDiaria(empresaId, almacen.Id, fecha, fuente, lineas.Count, pendientes.Count, usuarioId);
        foreach (var p in pendientes)
        {
            var movs = p.Subs.Select(s => foliosPorSub[s]).ToList();
            difs.Add(new DiferenciaConciliacion(empresaId, corrida.Id, almacen.Id, fecha, p.ArticuloId, p.Variante, p.Lote, p.Area,
                p.Teorico, p.Fisico, p.Costo, p.Valor, movs.Count > 0 ? movs[0].Id : null,
                movs.Count > 0 ? string.Join(", ", movs.Select(m => m.Folio)) : null));
        }
        db.ConciliacionesDiarias.Add(corrida);
        db.DiferenciasConciliacion.AddRange(difs);
        return (corrida, difs);
    }
}

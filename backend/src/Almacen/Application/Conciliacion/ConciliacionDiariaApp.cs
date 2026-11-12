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

/// <summary>M-225: mensaje de la app o consumo por composición del día que todavía no movió el saldo (en revisión, error o pendiente).</summary>
public sealed record EventoPendienteDia(string Tipo, string Referencia, string Estado, string? Error, DateTimeOffset RecibidoAt);

/// <summary>M-225: consumo de la mesa de corte (salida de consumo) o merma de proceso registrados ese día en la sucursal.</summary>
public sealed record EventoMesaDia(string Tipo, string Referencia, Guid ArticuloId, decimal Cantidad, string? Mesa, string Estado);

public sealed record EventosDelDia(IReadOnlyList<EventoPendienteDia> Pendientes, IReadOnlyList<EventoMesaDia> Mesa);

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
        IReadOnlyList<FisicoLinea> lineas, CancellationToken ct, bool confirmarPendientes = false,
        IReadOnlyCollection<string>? clavesSucursal = null)
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
        // M-225: con mensajes de la app o consumos de ese día sin aplicar, el faltante podría ser un evento que todavía no entra.
        // La corrida avisa y no baja nada hasta que el usuario confirma.
        var pendientesDia = await PendientesDelDiaAsync(db, fecha, ct, clavesSucursal);
        if (pendientesDia.Count > 0 && !confirmarPendientes)
            throw new BusinessRuleException("CONCILIACION_EVENTOS_PENDIENTES",
                $"Hay {pendientesDia.Count} mensaje(s) de la app o consumo(s) del {fecha:dd/MM/yyyy} sin aplicar (en revisión, con error o " +
                "pendientes de autorizar). Revísalos antes de bajar un faltante, o confirma para conciliar de todos modos.");

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
        // M-225: teórico al cierre del día elegido = saldo de hoy menos lo que se movió después de ese día (entradas restan, salidas suman).
        var posteriores = fecha < hoy ? await NetoPosteriorAsync(db, almacenId, ids, fecha, ct) : [];

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
            var teoricoHoy = filas.Sum(s => s.CantidadDisponible);
            var teoricoBase = teoricoHoy - posteriores.GetValueOrDefault((l.ArticuloId, l.ProductoAwVarianteId, lote));
            var teorico = area is { } a ? Math.Round(teoricoBase / a, 4) : teoricoBase;
            if (teorico == l.Fisico) continue;

            decimal costo = 0, valor = 0;
            var subs = new List<Guid>();
            if (l.Fisico < teorico)
            {
                var resta = Math.Round((teorico - l.Fisico) * (area ?? 1m), 4);
                resta = Math.Min(resta, teoricoHoy);
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

    /// <summary>Neto (entradas − salidas) de los movimientos registrados con fecha posterior a <paramref name="fecha"/> en el almacén.</summary>
    public static async Task<Dictionary<(Guid, Guid?, string?), decimal>> NetoPosteriorAsync(AlmacenDbContext db, Guid almacenId,
        Guid[] articulos, DateOnly fecha, CancellationToken ct)
    {
        var filas = await (from l in db.LineasMovimiento
                           join m in db.Movimientos on l.MovimientoId equals m.Id
                           join u in db.Ubicaciones on l.UbicacionId equals u.Id
                           join sub in db.SubAlmacenes on u.SubAlmacenId equals sub.Id
                           where sub.AlmacenId == almacenId && articulos.Contains(l.ArticuloId) && m.FechaMovimiento > fecha
                               && (m.Estado == EstadoMovimiento.Registrado || m.Estado == EstadoMovimiento.Cancelado)
                               && m.Tipo != TipoMovimiento.AjustePrecioFactura
                               && sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                           select new { l.ArticuloId, l.ProductoAwVarianteId, l.Lote, l.Cantidad, m.Tipo }).ToListAsync(ct);
        return filas.GroupBy(f => (f.ArticuloId, f.ProductoAwVarianteId, (string?)f.Lote))
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Tipo.EsEntrada() ? f.Cantidad : -f.Cantidad));
    }

    /// <summary>M-225: mensajes de la app y consumos por composición recibidos ese día que no movieron saldo.</summary>
    /// <param name="clavesSucursal">Claves de la sucursal del almacén (Clave y ClaveAw): solo cuentan los mensajes de esa sucursal
    /// (<c>datos.sucursal</c>) y los consumos de su clave. Null = todas.</param>
    public static async Task<IReadOnlyList<EventoPendienteDia>> PendientesDelDiaAsync(AlmacenDbContext db, DateOnly fecha, CancellationToken ct,
        IReadOnlyCollection<string>? clavesSucursal = null)
    {
        var desde = FechaNegocio.InicioUtc(fecha);
        var hasta = FechaNegocio.InicioUtc(fecha.AddDays(1));
        var mensajes = await db.MensajesApp.AsNoTracking()
            .Where(m => m.RecibidoAt >= desde && m.RecibidoAt < hasta && !m.Tipo.StartsWith("REQUISICION_")
                && (m.Estado == AppAlmacenes.SalidaAppAlmacenes.EnRevision || m.Estado == "ERROR" || m.Estado == Domain.Idempotencia.MensajeApp.Pendiente))
            .Select(m => new { m.Tipo, m.Referencia, m.MensajeId, m.Estado, m.Error, m.RecibidoAt, m.Payload }).ToListAsync(ct);
        var consumos = await db.ConsumosComponenteAw.AsNoTracking()
            .Where(c => c.RegistradoAt >= desde && c.RegistradoAt < hasta
                && (c.Estado == Domain.Consumo.ConsumoComponenteAw.EnRevision || c.Estado == Domain.Consumo.ConsumoComponenteAw.Excepcion))
            .Select(c => new { c.Auftnr, c.Pos, c.Estado, c.Error, c.RegistradoAt, c.ClaveSucursal })
            .ToListAsync(ct);
        bool DeLaSucursal(string? clave) => clavesSucursal is null
            || (clave is not null && clavesSucursal.Contains(clave.Trim(), StringComparer.OrdinalIgnoreCase));
        return
        [
            .. mensajes.Where(m => DeLaSucursal(SucursalDelPayload(m.Payload)))
                .Select(m => new EventoPendienteDia(m.Tipo, m.Referencia ?? m.MensajeId, m.Estado, m.Error, m.RecibidoAt)),
            .. consumos.Where(c => DeLaSucursal(c.ClaveSucursal))
                .Select(c => new EventoPendienteDia(Domain.Consumo.ConsumoComponenteAw.Tipo, $"{c.Auftnr} pos {c.Pos}", c.Estado, c.Error, c.RegistradoAt)),
        ];
    }

    private static string? SucursalDelPayload(string payload)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            return doc.RootElement.TryGetProperty("datos", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Object
                && d.TryGetProperty("sucursal", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String ? s.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// M-225: lo que hay que ver antes de bajar un faltante: los consumos de la mesa de corte (salidas de consumo) y las mermas de proceso
    /// de ese día en la sucursal del almacén, más lo que quedó sin aplicar. Sirve para encontrar un evento omitido o duplicado.
    /// </summary>
    public static async Task<EventosDelDia> EventosDelDiaAsync(AlmacenDbContext db, Guid almacenId, DateOnly fecha, CancellationToken ct,
        IReadOnlyCollection<string>? clavesSucursal = null)
    {
        var almacen = await db.Almacenes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == almacenId, ct)
            ?? throw new EntityNotFoundException("ALMACEN_NO_ENCONTRADO", $"No existe el almacén '{almacenId}'.");
        var consumos = await (from l in db.LineasMovimiento
                              join m in db.Movimientos on l.MovimientoId equals m.Id
                              join u in db.Ubicaciones on l.UbicacionId equals u.Id
                              join sub in db.SubAlmacenes on u.SubAlmacenId equals sub.Id
                              where sub.AlmacenId == almacenId && m.FechaMovimiento == fecha && m.Tipo == TipoMovimiento.SalidaConsumo
                                  && m.Estado == EstadoMovimiento.Registrado
                              select new EventoMesaDia("CONSUMO_MESA", m.Folio ?? "", l.ArticuloId, l.Cantidad, null, "REGISTRADO"))
            .ToListAsync(ct);
        var mermas = await db.MermasProceso.AsNoTracking()
            .Where(x => x.SucursalId == almacen.SucursalId && x.Fecha == fecha)
            .Select(x => new EventoMesaDia(x.Tipo, x.MensajeId ?? x.Id.ToString(), x.ArticuloId, x.M2, x.Mesa, x.Estado)).ToListAsync(ct);
        return new EventosDelDia(await PendientesDelDiaAsync(db, fecha, ct, clavesSucursal), [.. consumos, .. mermas]);
    }
}

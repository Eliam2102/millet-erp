using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.PublicPorts;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Infrastructure.PublicAdapters;
public sealed class RecepcionesCostoReadAdapter(AlmacenDbContext db, Millet.SharedKernel.Application.UnidadesMedida.IConversionUnidadPort conversion) : IRecepcionesCostoReadPort
{
    public async Task<IReadOnlyList<MovimientoOcLectura>> LeerMovimientosAsync(Guid empresa,
        IReadOnlyCollection<PartidaOcLectura> partidas, DateOnly corte, CancellationToken ct)
    {
        if (partidas.Count == 0) return [];
        var ocs = partidas.Select(p => p.OrdenCompraId).Distinct().ToArray();
        var entradas = await db.Movimientos.AsNoTracking().Include(m => m.Lineas)
            .Where(m => m.EmpresaId == empresa && m.OcId != null && ocs.Contains(m.OcId.Value)
                && m.Tipo == TipoMovimiento.EntradaCompra && m.Estado == EstadoMovimiento.Registrado
                && m.FechaMovimiento <= corte).ToListAsync(ct);
        if (entradas.SelectMany(m => m.Lineas).Any(l => l.LineaOcId == null))
            throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException("COMPRAS_RECEPCION_SIN_PARTIDA",
                "Por confirmar: hay recepciones sin vínculo a la partida de compra. Completa su trazabilidad antes de conciliar.");
        var devoluciones = await (from d in db.DevolucionesProveedor.AsNoTracking()
            join m in db.Movimientos on d.MovimientoSalidaId equals (Guid?)m.Id
            join l in db.LineasDevolucionProveedor on d.Id equals l.DevolucionId
            join origen in db.LineasMovimiento on l.LineaRecepcionOrigenId equals (Guid?)origen.Id
            where d.EmpresaId == empresa && d.OrdenCompraOrigenId != null && ocs.Contains(d.OrdenCompraOrigenId.Value)
                && m.Estado == EstadoMovimiento.Registrado && m.FechaMovimiento <= corte
            select new { d.Id, m.Folio, m.FechaMovimiento, origen.LineaOcId, l.Cantidad }).ToListAsync(ct);
        var activos = await (from a in db.RecepcionesActivos.AsNoTracking()
            join m in db.Movimientos on a.RecepcionId equals m.Id
            where a.EmpresaId == empresa && ocs.Contains(a.OrdenCompraId)
                && m.Estado == EstadoMovimiento.Registrado && m.FechaMovimiento <= corte
            select new { a.LineaOcId, m.Id, m.Folio, m.FechaMovimiento, a.Cantidad }).ToListAsync(ct);
        var resultado = new List<MovimientoOcLectura>();
        foreach (var p in partidas)
        {
            var recibidas = entradas.SelectMany(m => m.Lineas.Where(l => l.LineaOcId == p.LineaId)
                .Select(l => new MovimientoOcLectura(p.LineaId, m.Id, m.Folio ?? "Recepción sin folio", m.FechaMovimiento, l.Cantidad, false)));
            var devueltas = devoluciones.Where(d => d.LineaOcId == p.LineaId).Select(d =>
                new MovimientoOcLectura(p.LineaId, d.Id, d.Folio ?? "Devolución sin folio", d.FechaMovimiento, d.Cantidad, true));
            var movimientos = recibidas.Concat(devueltas).ToArray();
            if (movimientos.Length > 0)
            {
                var factor = await Millet.Almacen.Application.Recepciones.RecibidoPorLineaOc.FactorDocumentoABaseAsync(
                    conversion, p.ArticuloId, p.Unidad, ct);
                resultado.AddRange(movimientos.Select(m => m with { Cantidad = Math.Round(m.Cantidad / factor, 4) }));
            }
            resultado.AddRange(activos.Where(a => a.LineaOcId == p.LineaId).Select(a =>
                new MovimientoOcLectura(p.LineaId, a.Id, a.Folio ?? "Recepción de activo", a.FechaMovimiento, a.Cantidad, false)));
        }
        return resultado;
    }

    public async Task<IReadOnlyList<RecepcionCostoLectura>> ListarAsync(Guid empresa, Guid sucursal, Guid oc, CancellationToken ct)
    {
        var entradas = await (from m in db.Movimientos.AsNoTracking() join l in db.LineasMovimiento on m.Id equals l.MovimientoId
            join u in db.Ubicaciones on l.UbicacionId equals u.Id join s in db.SubAlmacenes on u.SubAlmacenId equals s.Id
            join a in db.Almacenes on s.AlmacenId equals a.Id
            where m.EmpresaId == empresa && m.OcId == oc && m.Tipo == TipoMovimiento.EntradaCompra && m.Estado == EstadoMovimiento.Registrado && a.SucursalId == sucursal
            orderby m.FechaRegistro, l.Id
            select new { Linea = l, Recepcion = m }).ToListAsync(ct);
        List<RecepcionCostoLectura> resultado = [];
        foreach (var entrada in entradas)
        {
            var l = entrada.Linea;
            var saldo = await db.SaldosInventario.AsNoTracking().SingleOrDefaultAsync(s => s.UbicacionId == l.UbicacionId && s.ArticuloId == l.ArticuloId, ct);
            var remanente = await RemanenteAsync(db, empresa, l.Id, l.UbicacionId!.Value, l.ArticuloId, ct);
            resultado.Add(new(l.Id, oc, entrada.Recepcion.Id, l.ArticuloId, l.UbicacionId.Value,
                (entrada.Recepcion.Folio ?? "Recepción") + " · partida " + l.Posicion, l.Cantidad,
                Math.Min(remanente, saldo?.Cantidad ?? 0), saldo?.Cantidad ?? 0, saldo?.CostoPromedioMxn ?? 0));
        }
        return resultado;
    }
    public static async Task<decimal> RemanenteAsync(AlmacenDbContext db, Guid empresa, Guid partida, Guid ubicacion, Guid articulo, CancellationToken ct)
    {
        var historial = await (from m in db.Movimientos.AsNoTracking() join l in db.LineasMovimiento on m.Id equals l.MovimientoId
            where m.EmpresaId == empresa && m.Estado == EstadoMovimiento.Registrado && l.UbicacionId == ubicacion && l.ArticuloId == articulo
            orderby m.FechaRegistro, m.Id, l.Posicion
            select new { l.Id, l.Cantidad, m.Tipo }).ToListAsync(ct);
        decimal existencia = 0, remanente = 0;
        // Sin lotes físicos: cada salida consume proporcionalmente las entradas del pool de costo promedio.
        // Una entrada posterior no repone el remanente de una recepción ya consumida.
        foreach (var h in historial)
        {
            if (h.Tipo == TipoMovimiento.AjustePrecioFactura) continue;
            if (h.Tipo.EsEntrada()) { existencia += h.Cantidad; if (h.Id == partida) remanente += h.Cantidad; }
            else
            {
                remanente *= existencia > 0 ? Math.Max(0, (existencia - h.Cantidad) / existencia) : 0;
                existencia = Math.Max(0, existencia - h.Cantidad);
            }
        }
        return remanente;
    }
}

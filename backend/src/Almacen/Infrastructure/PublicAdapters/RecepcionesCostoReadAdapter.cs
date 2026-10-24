using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.PublicPorts;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Infrastructure.PublicAdapters;
public sealed class RecepcionesCostoReadAdapter(AlmacenDbContext db) : IRecepcionesCostoReadPort
{
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
            var saldo = await db.SaldosInventario.AsNoTracking().SingleOrDefaultAsync(s => s.UbicacionId == l.UbicacionId && s.ArticuloId == l.ArticuloId && s.ProductoAwVarianteId == l.ProductoAwVarianteId, ct);
            var remanente = await RemanenteAsync(db, empresa, l.Id, l.UbicacionId!.Value, l.ArticuloId, l.ProductoAwVarianteId, ct);
            resultado.Add(new(l.Id, oc, entrada.Recepcion.Id, l.ArticuloId, l.UbicacionId.Value,
                (entrada.Recepcion.Folio ?? "Recepción") + " · partida " + l.Posicion, l.Cantidad,
                Math.Min(remanente, saldo?.Cantidad ?? 0), saldo?.Cantidad ?? 0, saldo?.CostoPromedioMxn ?? 0));
        }
        return resultado;
    }
    public static async Task<decimal> RemanenteAsync(AlmacenDbContext db, Guid empresa, Guid partida, Guid ubicacion, Guid articulo, Guid? variante, CancellationToken ct)
    {
        var historial = await (from m in db.Movimientos.AsNoTracking() join l in db.LineasMovimiento on m.Id equals l.MovimientoId
            where m.EmpresaId == empresa && m.Estado == EstadoMovimiento.Registrado && l.UbicacionId == ubicacion && l.ArticuloId == articulo && l.ProductoAwVarianteId == variante
            orderby m.FechaRegistro, m.Id, l.Posicion
            select new { l.Id, l.Cantidad, m.Tipo }).ToListAsync(ct);
        decimal existencia = 0, remanente = 0;
        // Sin lotes físicos: cada salida consume proporcionalmente las entradas del pool de costo promedio.
        // Una entrada posterior no repone el remanente de una recepción ya consumida.
        foreach (var h in historial)
        {
            // La recepción cancelada ya no entra al historial (solo Registrado): su CEN tampoco debe restar al remanente de las demás.
            if (h.Tipo is TipoMovimiento.AjustePrecioFactura or TipoMovimiento.CancelacionEntradaCompra) continue;
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

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Application.PublicPorts;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Infrastructure.PublicAdapters;
public sealed class RecepcionesCostoReadAdapter(AlmacenDbContext db, ILogger<RecepcionesCostoReadAdapter> logger) : IRecepcionesCostoReadPort
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
            var saldo = await db.SaldosInventario.AsNoTracking().SingleOrDefaultAsync(s => s.UbicacionId == l.UbicacionId && s.ArticuloId == l.ArticuloId, ct);
            var remanentes = await RemanentesAsync(db, empresa, l.Id, l.ArticuloId, logger, ct);
            var destinos = remanentes.Keys.ToList();
            var saldos = await db.SaldosInventario.AsNoTracking()
                .Where(s => s.ArticuloId == l.ArticuloId && destinos.Contains(s.UbicacionId)).ToListAsync(ct);
            var remanente = saldos.Sum(s => Math.Min(s.Cantidad, remanentes[s.UbicacionId]));
            resultado.Add(new(l.Id, oc, entrada.Recepcion.Id, l.ArticuloId, l.UbicacionId!.Value,
                (entrada.Recepcion.Folio ?? "Recepción") + " · partida " + l.Posicion, l.Cantidad,
                remanente, saldo?.Cantidad ?? 0, saldo?.CostoPromedioMxn ?? 0));
        }
        return resultado;
    }
    public static async Task<IReadOnlyDictionary<Guid, decimal>> RemanentesAsync(
        AlmacenDbContext db, Guid empresa, Guid partida, Guid articulo, ILogger logger, CancellationToken ct)
    {
        var historial = await (from m in db.Movimientos.AsNoTracking() join l in db.LineasMovimiento on m.Id equals l.MovimientoId
            where m.EmpresaId == empresa && m.Estado == EstadoMovimiento.Registrado && l.UbicacionId != null && l.ArticuloId == articulo
            orderby m.FechaRegistro, m.Id, l.Posicion
            select new { l.Id, l.MovimientoId, l.Posicion, l.UbicacionId, l.Cantidad, m.Tipo, m.SalidaOrigenId }).ToListAsync(ct);
        var dictamenes = await db.DictamenesRevision.AsNoTracking()
            .Where(d => d.EmpresaId == empresa && d.MovimientoSalidaRevisionId != null && d.MovimientoEntradaId != null)
            .Select(d => new { Salida = d.MovimientoSalidaRevisionId, Entrada = d.MovimientoEntradaId }).ToListAsync(ct);
        Dictionary<Guid, decimal> existencias = [], remanentes = [];
        HashSet<Guid> entradasTrasladadas = [];
        foreach (var h in historial)
        {
            if (h.Tipo == TipoMovimiento.AjustePrecioFactura || entradasTrasladadas.Contains(h.Id)) continue;
            var ubicacion = h.UbicacionId!.Value;
            var existencia = existencias.GetValueOrDefault(ubicacion);
            var remanente = remanentes.GetValueOrDefault(ubicacion);
            if (h.Tipo.EsEntrada())
            {
                existencias[ubicacion] = existencia + h.Cantidad;
                if (h.Id == partida) remanentes[ubicacion] = remanente + h.Cantidad;
                continue;
            }
            // Cada salida toma su proporción del pool; las entradas nuevas no reponen esta partida.
            var retirado = remanente * (existencia > 0 ? Math.Min(1, h.Cantidad / existencia) : 1);
            remanentes[ubicacion] = remanente - retirado;
            existencias[ubicacion] = Math.Max(0, existencia - h.Cantidad);
            if (!h.Tipo.EsTraslado()) continue;
            var pares = historial.Where(e => e.Tipo == TipoMovimiento.ReincorporacionTrasRevision &&
                e.Posicion == h.Posicion && e.Cantidad == h.Cantidad &&
                (e.SalidaOrigenId == h.MovimientoId || dictamenes.Any(d => d.Salida == h.MovimientoId && d.Entrada == e.MovimientoId))).ToList();
            if (pares.Count != 1 || historial.IndexOf(pares[0]) <= historial.IndexOf(h) || !entradasTrasladadas.Add(pares[0].Id))
            {
                logger.LogWarning("Traslado {MovimientoId}, línea {LineaId} sin par inequívoco; se trata como salida real", h.MovimientoId, h.Id);
                continue;
            }
            var destino = pares[0].UbicacionId!.Value;
            existencias[destino] = existencias.GetValueOrDefault(destino) + h.Cantidad;
            remanentes[destino] = remanentes.GetValueOrDefault(destino) + retirado;
        }
        return remanentes.Where(x => x.Value > 0).ToDictionary(x => x.Key, x => x.Value);
    }
}

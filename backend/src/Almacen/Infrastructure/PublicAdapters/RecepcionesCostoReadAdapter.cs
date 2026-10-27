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
            // Existencia y costo de donde está hoy la partida; sin remanente, los de la ubicación de recepción.
            if (saldos.Count == 0 && saldo is not null) saldos = [saldo];
            var existencia = saldos.Sum(s => s.Cantidad);
            var costoPromedio = existencia > 0 ? saldos.Sum(s => s.Cantidad * s.CostoPromedioMxn) / existencia : saldo?.CostoPromedioMxn ?? 0;
            resultado.Add(new(l.Id, oc, entrada.Recepcion.Id, l.ArticuloId, l.UbicacionId!.Value,
                (entrada.Recepcion.Folio ?? "Recepción") + " · partida " + l.Posicion, l.Cantidad,
                remanente, existencia, costoPromedio));
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
        var salidasTraslado = historial.Where(h => h.Tipo.EsTraslado()).Select(h => (Guid?)h.MovimientoId).Distinct().ToList();
        var dictamenes = await db.DictamenesRevision.AsNoTracking()
            .Where(d => d.EmpresaId == empresa && salidasTraslado.Contains(d.MovimientoSalidaRevisionId) && d.MovimientoEntradaId != null)
            .Select(d => new { Salida = d.MovimientoSalidaRevisionId, Entrada = d.MovimientoEntradaId }).ToListAsync(ct);
        // Índices para no recorrer el historial por cada traslado: posición de cada línea y reincorporaciones por (salida, posición).
        var indice = historial.Select((h, i) => (h.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var salidasPorEntrada = dictamenes.ToLookup(d => d.Entrada, d => d.Salida);
        var reincorporaciones = historial.Where(e => e.Tipo == TipoMovimiento.ReincorporacionTrasRevision)
            .SelectMany(e => salidasPorEntrada[e.MovimientoId].Append(e.SalidaOrigenId).OfType<Guid>().Distinct()
                .Select(salida => (Salida: salida, Entrada: e)))
            .ToLookup(x => (x.Salida, x.Entrada.Posicion), x => x.Entrada);
        Dictionary<Guid, decimal> existencias = [], remanentes = [];
        HashSet<Guid> entradasTrasladadas = [];
        foreach (var h in historial)
        {
            // La recepción cancelada ya no entra al historial (solo Registrado): su CEN tampoco debe restar al remanente de las demás.
            if (h.Tipo is TipoMovimiento.AjustePrecioFactura or TipoMovimiento.CancelacionEntradaCompra || entradasTrasladadas.Contains(h.Id)) continue;
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
            var pares = reincorporaciones[(h.MovimientoId, h.Posicion)].Where(e => e.Cantidad == h.Cantidad).ToList();
            if (pares.Count != 1 || indice[pares[0].Id] <= indice[h.Id] || !entradasTrasladadas.Add(pares[0].Id))
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

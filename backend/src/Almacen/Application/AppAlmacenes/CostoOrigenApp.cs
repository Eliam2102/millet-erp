using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>
/// De dónde nació un movimiento de la app (contrato v9, <c>datos.origen</c>): <c>Referencia</c> es la referencia del mensaje original
/// en <c>alm_mensaje</c> — la salida del préstamo o de la venta (<c>salidaOriginal</c>, MV-…) o la entrada cancelada o reclamada
/// (<c>entrada</c>, EN-…).
/// </summary>
public sealed record OrigenApp(string Tipo, string Referencia);

/// <summary>
/// Costo con que se movió el material en el mensaje original (v9): el regreso de préstamo y la devolución de venta entran al costo con
/// que salieron; la cancelación de una entrada y la reclamación al proveedor salen al costo con que entraron. Se busca el mensaje
/// original ya aplicado por su referencia, sus movimientos del ERP por folio y, en ellos, la misma medida (primero el mismo lote).
/// Si el ERP no lo tiene (p. ej. la venta por albarán no pasa por la app, o el original es anterior al ERP) no hay costo de origen y
/// el llamador usa el promedio del ERP.
/// </summary>
internal static class CostoOrigenApp
{
    internal sealed record LineaOrigen(Guid ArticuloId, Guid? Variante, string? Lote, decimal Cantidad, decimal Costo);

    internal static async Task<IReadOnlyList<LineaOrigen>> LeerAsync(AlmacenDbContext db, OrigenApp? origen, CancellationToken ct)
    {
        if (origen is null || string.IsNullOrWhiteSpace(origen.Referencia)) return [];
        var referencia = origen.Referencia.Trim();
        var folios = (await db.MensajesApp.AsNoTracking()
                .Where(m => m.Referencia == referencia && m.Folio != null
                    && (m.Estado == SalidaAppAlmacenes.Recibido || m.Estado == "DUPLICADO"))
                .Select(m => m.Folio!).ToListAsync(ct))
            // Una salida que tocó varios sub-almacenes guarda sus folios separados por coma.
            .SelectMany(f => f.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct().ToList();
        if (folios.Count == 0) return [];
        return await (from m in db.Movimientos.AsNoTracking()
                      join l in db.LineasMovimiento.AsNoTracking() on m.Id equals l.MovimientoId
                      where m.Folio != null && folios.Contains(m.Folio) && m.Estado == EstadoMovimiento.Registrado
                      select new LineaOrigen(l.ArticuloId, l.ProductoAwVarianteId, l.Lote, l.Cantidad, l.CostoUnitarioMxn))
            .ToListAsync(ct);
    }

    /// <summary>Costo de la misma medida en el original (ponderado por cantidad), primero del mismo lote; null si no está.</summary>
    internal static decimal? Costo(IReadOnlyList<LineaOrigen> lineas, Guid articuloId, Guid? variante, string? lote)
    {
        var misma = lineas.Where(l => l.ArticuloId == articuloId && l.Variante == variante).ToList();
        var normalizado = LineaMovimiento.NormalizarLote(lote);
        var delLote = misma.Where(l => l.Lote == normalizado).ToList();
        var usar = delLote.Count > 0 ? delLote : misma;
        var cantidad = usar.Sum(l => l.Cantidad);
        return cantidad > 0 ? Math.Round(usar.Sum(l => l.Cantidad * l.Costo) / cantidad, 4) : null;
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Revision;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Revision;

/// <summary>
/// ALM-04: lo que está en MAT-REV por una recepción sin dictaminar está reservado para el dictamen
/// (Liberar/Baja/Rechazar con permiso <c>dictaminar</c>). Fórmula única del pendiente por línea de
/// revisión: en revisión − dictaminado (Liberar/Baja) − en devolución. La bandeja descuenta toda
/// devolución no rechazada (ya no está pendiente de dictaminar). Los consumos de MAT-REV del flujo 8.A
/// (baja por daño / reincorporar), que no deben saltarse el dictamen, descuentan solo las devoluciones
/// con salida física (Registrada / ConciliadaConNcFiscal): mientras la devolución está en Borrador,
/// EnAutorizacion o Autorizada la mercancía sigue en MAT-REV y su cantidad sigue reservada para esa salida.
/// </summary>
internal static class ReservaDictamen
{
    /// <summary>
    /// Cantidad en devolución a proveedor (no rechazada) por línea de recepción origen; con
    /// <paramref name="soloConSalidaFisica"/> solo las que ya sacaron la mercancía de MAT-REV.
    /// </summary>
    public static async Task<Dictionary<Guid, decimal>> EnDevolucionPorLineaAsync(
        AlmacenDbContext db, Guid[] lineaIds, CancellationToken ct, bool soloConSalidaFisica = false)
        => (await (
            from ld in db.LineasDevolucionProveedor.AsNoTracking()
            join d in db.DevolucionesProveedor.AsNoTracking() on ld.DevolucionId equals d.Id
            where ld.LineaRecepcionOrigenId != null && lineaIds.Contains(ld.LineaRecepcionOrigenId.Value)
                && (soloConSalidaFisica
                    ? d.Estado == EstadoDevolucionProveedor.Registrada || d.Estado == EstadoDevolucionProveedor.ConciliadaConNcFiscal
                    : d.Estado != EstadoDevolucionProveedor.Rechazada)
            select new { ld.LineaRecepcionOrigenId, ld.Cantidad }).ToListAsync(ct))
            .GroupBy(x => x.LineaRecepcionOrigenId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));

    public static bool CuentaComoDictaminada(ResultadoDictamen r) => r is ResultadoDictamen.Liberar or ResultadoDictamen.Baja;

    /// <summary>Pendiente de dictamen sumado por (bin, artículo) de los bins dados.</summary>
    public static async Task<Dictionary<(Guid Ubicacion, Guid Articulo), decimal>> PendientePorBinAsync(
        AlmacenDbContext db, Guid[] ubicacionIds, CancellationToken ct)
    {
        var lineas = await (
            from l in db.LineasMovimiento.AsNoTracking()
            join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
            where m.Tipo == TipoMovimiento.EntradaCompra && m.Estado == EstadoMovimiento.Registrado
                && l.UbicacionId != null && ubicacionIds.Contains(l.UbicacionId.Value)
            select new { l.Id, Ubicacion = l.UbicacionId!.Value, l.ArticuloId, l.Cantidad }).ToListAsync(ct);
        if (lineas.Count == 0) return [];

        var ids = lineas.Select(l => l.Id).ToArray();
        var devolucion = await EnDevolucionPorLineaAsync(db, ids, ct, soloConSalidaFisica: true);
        var dictaminado = (await db.DictamenesRevision.AsNoTracking()
            .Where(d => ids.Contains(d.LineaRevisionId)).ToListAsync(ct))
            .Where(d => CuentaComoDictaminada(d.Resultado))
            .GroupBy(d => d.LineaRevisionId).ToDictionary(g => g.Key, g => g.Sum(d => d.Cantidad));

        return lineas
            .GroupBy(l => (l.Ubicacion, l.ArticuloId))
            .ToDictionary(g => g.Key, g => g.Sum(l =>
                Math.Max(0m, l.Cantidad - dictaminado.GetValueOrDefault(l.Id) - devolucion.GetValueOrDefault(l.Id))));
    }

    /// <summary>
    /// Rechaza (422 <c>MATREV_RESERVADO_PARA_DICTAMEN</c>) si consumir <paramref name="consumos"/> de MAT-REV
    /// dejaría menos saldo que lo reservado para dictamen. Serializa por bin (advisory lock transaccional);
    /// el llamador debe tener ya su transacción abierta o confirmar la que devuelve el lock.
    /// </summary>
    public static async Task<IReadOnlyDictionary<(Guid Ubicacion, Guid Articulo), decimal>> ValidarAsync(
        AlmacenDbContext db, IReadOnlyCollection<(Guid Ubicacion, Guid Articulo, decimal Cantidad)> consumos,
        CancellationToken ct)
    {
        var bins = consumos.Select(c => c.Ubicacion).Distinct().Order().ToArray();
        foreach (var bin in bins) await DevolucionOrigenLock.AbrirAsync(db, bin, ct);

        var pendiente = await PendientePorBinAsync(db, bins, ct);
        foreach (var g in consumos.GroupBy(c => (c.Ubicacion, c.Articulo)))
        {
            var reservado = pendiente.GetValueOrDefault(g.Key);
            if (reservado <= 0) continue; // sin reserva: el saldo lo valida el trigger (SALDO_INSUFICIENTE)
            var saldo = await db.SaldosInventario.AsNoTracking()
                .Where(s => s.UbicacionId == g.Key.Ubicacion && s.ArticuloId == g.Key.Articulo)
                .Select(s => (decimal?)s.Cantidad).FirstOrDefaultAsync(ct) ?? 0m;
            var pedido = g.Sum(c => c.Cantidad);
            if (saldo < pedido) continue; // falta saldo aunque no hubiera reserva: lo reporta el trigger (SALDO_INSUFICIENTE)
            if (saldo - reservado < pedido)
                throw new BusinessRuleException("MATREV_RESERVADO_PARA_DICTAMEN",
                    $"En esa ubicación hay {reservado:0.####} reservados para dictamen de una recepción en revisión; " +
                    $"solo hay {Math.Max(0m, saldo - reservado):0.####} disponibles para esta operación. " +
                    "Dictamina primero el material de la recepción (permiso dictaminar).");
        }
        return pendiente;
    }
}

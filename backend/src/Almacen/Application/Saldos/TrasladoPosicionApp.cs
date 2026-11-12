using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Saldos;

/// <param name="SaldoId">Fila de saldo de origen (hoja/medida/lote en su posición).</param>
/// <param name="Version">Marca del saldo que vio la pantalla (<see cref="TrasladoPosicionApp.Version"/>); si cambió, se pide recargar.</param>
/// <param name="Cantidad">Hojas enteras si la fila tiene medida; si no, en la unidad del artículo.</param>
public sealed record TrasladarPosicion(Guid SaldoId, string Version, decimal Cantidad, Guid UbicacionDestinoId, string? Motivo);

public sealed record TrasladoPosicionResultado(Guid MovimientoId, string Folio, string? FolioEntrada);

/// <summary>
/// M-231: traslado de una hoja (o de existencia sin medida) entre posiciones del mismo almacén. Es un traslado interno, como el
/// CAMBIO_ALMACEN de la app: salida <see cref="TipoMovimiento.TrasladoInterno"/> de la posición origen y entrada
/// <see cref="TipoMovimiento.EntradaTrasladoInterno"/> ligada en la destino, mismo lote, misma medida y mismo costo promedio, sin evento
/// contable (no hace póliza). El folio del traslado es el de la salida; la entrada queda ligada a ella (salida_origen_id). La posición destino debe
/// tener asignado el artículo. Si el saldo cambió desde que la pantalla lo leyó (otro usuario movió la misma hoja), responde conflicto
/// para recargar en vez de mover dos veces. No guarda: el llamador abre la transacción, hace <c>SaveChangesAsync</c> y confirma.
/// </summary>
public static class TrasladoPosicionApp
{
    /// <summary>Marca de versión de un saldo: cambia con cada movimiento que lo toca. Texto (no número): los ticks no caben exactos en JS.</summary>
    public static string Version(DateTimeOffset ultimaActualizacionAt) =>
        ultimaActualizacionAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static async Task<TrasladoPosicionResultado> RegistrarAsync(AlmacenDbContext db, IVarianteVidrioReadPort variantes,
        IArticuloReadPort articulos, IPeriodoContableReadPort periodo, Guid empresaId, Guid usuarioId, TrasladarPosicion c, CancellationToken ct)
    {
        if (c.Cantidad <= 0)
            throw new BusinessRuleException("TRASLADO_CANTIDAD_INVALIDA", "La cantidad a trasladar debe ser mayor que cero.");
        var motivoUsuario = string.IsNullOrWhiteSpace(c.Motivo) ? null : c.Motivo.Trim();
        if (motivoUsuario?.Length > 300)
            throw new BusinessRuleException("TRASLADO_MOTIVO_LARGO", "El motivo no puede exceder 300 caracteres.");

        var ubicacionOrigen = await db.SaldosInventario.AsNoTracking().Where(s => s.Id == c.SaldoId)
            .Select(s => new { s.UbicacionId, s.ArticuloId }).FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("SALDO_NO_ENCONTRADO", "La existencia elegida ya no está; recarga la pantalla.");
        if (db.Database.IsNpgsql())
            // Mismo orden de bloqueo que el resto de las salidas: la segunda pantalla espera aquí y luego ve la versión nueva.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM almacen.saldos_inventario WHERE articulo_id = {ubicacionOrigen.ArticuloId} AND ubicacion_id = ANY({new[] { ubicacionOrigen.UbicacionId, c.UbicacionDestinoId }}) ORDER BY articulo_id, ubicacion_id, producto_aw_variante_id, lote FOR UPDATE", ct);
        var saldo = await db.SaldosInventario.AsNoTracking().FirstAsync(s => s.Id == c.SaldoId, ct);
        if (Version(saldo.UltimaActualizacionAt) != c.Version)
            throw new ConflictException("TRASLADO_SALDO_CAMBIO",
                "La existencia de esa posición cambió desde que abriste la pantalla (otro usuario la movió). Recarga y vuelve a intentarlo.");
        if (c.UbicacionDestinoId == saldo.UbicacionId)
            throw new BusinessRuleException("TRASLADO_MISMA_POSICION", "Elige una posición destino distinta de la de origen.");

        var (almacenOrigen, _) = await AlmacenSucursalResolver.ResolverAsync(db, saldo.SubAlmacenId, ct);
        var destino = await db.Ubicaciones.AsNoTracking().Where(u => u.Id == c.UbicacionDestinoId)
            .Select(u => new { u.SubAlmacenId, u.Clave }).FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("UBICACION_NO_ENCONTRADA", "La posición destino no existe.");
        var (almacenDestino, _) = await AlmacenSucursalResolver.ResolverAsync(db, destino.SubAlmacenId, ct);
        if (almacenDestino != almacenOrigen)
            throw new BusinessRuleException("TRASLADO_OTRO_ALMACEN", "La posición destino es de otro almacén; el traslado es entre posiciones del mismo almacén.");
        if (await db.SubAlmacenes.AsNoTracking().AnyAsync(s => (s.Id == saldo.SubAlmacenId || s.Id == destino.SubAlmacenId)
                && (s.Tipo == TipoSubAlmacen.MaterialEnRevision || s.Tipo == TipoSubAlmacen.Transitorio), ct))
            throw new BusinessRuleException("TRASLADO_DESDE_REVISION", "El material en revisión o en tránsito no se mueve entre posiciones.");
        if (await db.Set<BloqueoInventario>().AsNoTracking().AnyAsync(b => (b.SubAlmacenId == saldo.SubAlmacenId || b.SubAlmacenId == destino.SubAlmacenId)
                && b.Activo && b.BloqueaSalidas, ct))
            throw new BusinessRuleException("SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL", "El almacén está en conteo anual; no se mueven hojas hasta que se aplique.");
        // La posición destino debe tener asignado el artículo (misma regla que una entrada).
        await UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(db, c.UbicacionDestinoId, saldo.ArticuloId, ct);
        var hoy = FechaNegocio.Hoy(DateTimeOffset.UtcNow);
        await PeriodoCerradoValidator.LanzarSiCerradoAsync(db, empresaId, hoy, periodo, ct);

        var articulo = await articulos.ObtenerAsync(saldo.ArticuloId, ct);
        VarianteVidrioLectura? medida = null;
        var cantidad = c.Cantidad;
        if (saldo.ProductoAwVarianteId is Guid v)
        {
            if (c.Cantidad != decimal.Truncate(c.Cantidad))
                throw new BusinessRuleException("HOJAS_ENTERAS", "Las hojas se trasladan en números enteros.");
            medida = (await variantes.ObtenerPorIdsAsync([v], ct)).GetValueOrDefault(v);
            var area = medida?.AreaHojaM2 ?? throw new BusinessRuleException("VARIANTE_SIN_MEDIDA", "La medida de la hoja no tiene ancho y alto.");
            cantidad = Math.Round(c.Cantidad * area, 4);
        }
        if (cantidad > saldo.CantidadDisponible)
            throw new BusinessRuleException("TRASLADO_SALDO_INSUFICIENTE",
                $"En la posición hay {saldo.CantidadDisponible:0.####} disponibles y quieres mover {cantidad:0.####}.");

        var origenClave = await db.Ubicaciones.AsNoTracking().Where(u => u.Id == saldo.UbicacionId).Select(u => u.Clave).FirstAsync(ct);
        var motivo = $"Traslado entre posiciones {origenClave} → {destino.Clave}{(motivoUsuario is null ? "" : $": {motivoUsuario}")}";
        var linea = new LineaRevisionMov(saldo.ArticuloId, articulo?.UnidadMedida ?? (medida is null ? "PZA" : "M2"), cantidad, saldo.CostoPromedioMxn,
            saldo.UbicacionId, saldo.ProductoAwVarianteId, medida?.AnchoMm, medida?.AltoMm, saldo.Lote);
        var (salida, entrada) = await MovimientosRevision.AgregarParLiberacionAsync(db, empresaId, usuarioId, hoy, motivo,
            [new ParLiberacionLinea(linea, c.UbicacionDestinoId)], ct, TipoMovimiento.TrasladoInterno, TipoMovimiento.EntradaTrasladoInterno);
        return new TrasladoPosicionResultado(salida.Id, salida.Folio, entrada.Folio);
    }
}

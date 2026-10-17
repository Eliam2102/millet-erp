using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Revision;

internal sealed record LineaRevisionMov(
    Guid ArticuloId, string UnidadMedida, decimal Cantidad, decimal CostoUnitarioMxn, Guid UbicacionId);

/// <summary>Línea de un par de liberación: sale del bin de MAT-REV y entra al bin disponible destino.</summary>
internal sealed record ParLiberacionLinea(LineaRevisionMov Origen, Guid DestinoUbicacionId);

internal readonly record struct MovimientoCreado(Guid Id, string Folio);

/// <summary>
/// Construcción compartida de los movimientos de la cuarentena (ALM-04): baja por daño y el par de
/// liberación (salida <see cref="TipoMovimiento.SalidaPorLiberacionRevision"/> desde MAT-REV + entrada
/// <see cref="TipoMovimiento.ReincorporacionTrasRevision"/> al rack disponible). Un movimiento solo tiene
/// líneas de un sub-almacén, por eso la liberación son DOS movimientos. No guarda ni confirma:
/// el llamador abre la transacción antes (lock de folio) y hace un solo <c>SaveChangesAsync</c> + commit.
/// </summary>
internal static class MovimientosRevision
{
    public static async Task<MovimientoCreado> AgregarAsync(
        AlmacenDbContext db, Guid empresaId, Guid usuarioId, TipoMovimiento tipo, DateOnly fecha,
        string motivo, IReadOnlyList<LineaRevisionMov> lineas, CancellationToken ct)
    {
        var movimiento = new MovimientoInventario(Guid.CreateVersion7(), tipo, empresaId, fecha);
        typeof(MovimientoInventario).GetProperty("Motivo")!.SetValue(movimiento, motivo);

        var posicion = 1;
        foreach (var l in lineas)
        {
            movimiento.AgregarLinea(new LineaMovimiento(
                id: Guid.CreateVersion7(), movimientoId: movimiento.Id, posicion: posicion++,
                articuloId: l.ArticuloId, cantidad: l.Cantidad, unidadMedida: l.UnidadMedida,
                costoUnitarioMxn: l.CostoUnitarioMxn, ubicacionId: l.UbicacionId));
        }

        var anio = fecha.Year;
        var prefijo = tipo.PrefijoFolio();
        _ = await FolioMovimientoLock.AbrirAsync(db, prefijo, anio, ct); // la transacción ya la abrió el llamador
        var secuencia = await db.FolioSecuenciasMovimiento
            .FirstOrDefaultAsync(s => s.Prefijo == prefijo && s.Anio == anio, ct);
        if (secuencia is null)
        {
            secuencia = new FolioSecuenciaMovimiento(Guid.CreateVersion7(), prefijo, anio, 0);
            db.FolioSecuenciasMovimiento.Add(secuencia);
        }
        var folio = FolioMovimiento.Construir(tipo, anio, secuencia.Incrementar());
        movimiento.Registrar(folio, usuarioId);
        db.Movimientos.Add(movimiento);
        return new MovimientoCreado(movimiento.Id, folio.Valor);
    }

    public static async Task<(MovimientoCreado Salida, MovimientoCreado Entrada)> AgregarParLiberacionAsync(
        AlmacenDbContext db, Guid empresaId, Guid usuarioId, DateOnly fecha, string motivo,
        IReadOnlyList<ParLiberacionLinea> lineas, CancellationToken ct)
    {
        var salida = await AgregarAsync(db, empresaId, usuarioId, TipoMovimiento.SalidaPorLiberacionRevision,
            fecha, motivo, lineas.Select(l => l.Origen).ToList(), ct);
        var entrada = await AgregarAsync(db, empresaId, usuarioId, TipoMovimiento.ReincorporacionTrasRevision,
            fecha, motivo, lineas.Select(l => l.Origen with { UbicacionId = l.DestinoUbicacionId }).ToList(), ct);
        return (salida, entrada);
    }
}

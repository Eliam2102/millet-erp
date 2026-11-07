using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Roturas;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.Roturas;

public sealed record CapturarRoturaApp(Guid UbicacionId, Guid ArticuloId, decimal Cantidad, DateOnly Fecha, string MotivoCodigo, string? Comentario);

/// <summary>
/// ALM-03: captura y decisión de la salida por rotura. Capturar no mueve el saldo; autorizar crea el movimiento
/// <see cref="TipoMovimiento.SalidaPorRotura"/> a costo promedio vigente y avisa a Contabilidad. Las reglas comunes de salida
/// (periodo cerrado, inventario anual, un solo sub-almacén, saldo no negativo) se validan al capturar y otra vez al autorizar,
/// porque entre un paso y otro pueden cambiar.
/// </summary>
public static class SalidaRoturaApp
{
    public static async Task<SalidaRotura> CapturarAsync(AlmacenDbContext db, IPeriodoContableReadPort periodo, IDecimalesUnidadGuard decimales,
        IArticuloReadPort articulos, Guid empresaId, Guid usuarioId, CapturarRoturaApp c, CancellationToken ct)
    {
        var ubicacion = await db.Ubicaciones.AsNoTracking().Where(u => u.Id == c.UbicacionId)
            .Select(u => new { u.SubAlmacenId }).FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("ROTURA_UBICACION_INVALIDA", "La ubicación elegida no existe.");
        var sucursalId = await ValidarReglasDeSalidaAsync(db, periodo, empresaId, ubicacion.SubAlmacenId, c.Fecha, ct);
        var articulo = await articulos.ObtenerAsync(c.ArticuloId, ct)
            ?? throw new BusinessRuleException("ROTURA_ARTICULO_INVALIDO", "El artículo elegido no existe.");
        await decimales.ValidarAsync([new CantidadAValidar(c.ArticuloId, c.Cantidad, articulo.UnidadMedida)], ct);
        // ponytail: solo existencia sin medida ni lote; la rotura por medida y lote llega con A4.1/A4.2.
        if (!await db.SaldosInventario.AsNoTracking().AnyAsync(s => s.UbicacionId == c.UbicacionId && s.ArticuloId == c.ArticuloId
                && s.ProductoAwVarianteId == null && s.CantidadDisponible > 0, ct))
            throw new BusinessRuleException("ROTURA_SIN_EXISTENCIA", "El artículo no tiene existencia disponible (sin medida) en esa ubicación.");

        var rotura = new SalidaRotura(empresaId, sucursalId, ubicacion.SubAlmacenId, c.UbicacionId, c.ArticuloId, c.Cantidad,
            articulo.UnidadMedida, c.Fecha, c.MotivoCodigo, c.Comentario, usuarioId);
        db.SalidasRotura.Add(rotura);
        return rotura;
    }

    /// <summary>Autoriza: movimiento + evento + estado en una sola transacción. Guarda y confirma.</summary>
    public static async Task AutorizarAsync(AlmacenDbContext db, IIntegrationEventPublisher events, IPeriodoContableReadPort periodo,
        SalidaRotura r, Guid usuarioId, CancellationToken ct)
    {
        r.VerificarAutorizacion(usuarioId);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;
        await ValidarReglasDeSalidaAsync(db, periodo, r.EmpresaId, r.SubAlmacenId, r.Fecha, ct);
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM almacen.saldos_inventario WHERE articulo_id = {r.ArticuloId} AND ubicacion_id = {r.UbicacionId} ORDER BY articulo_id, ubicacion_id, producto_aw_variante_id, lote FOR UPDATE", ct);
        var saldo = await db.SaldosInventario.AsNoTracking().FirstOrDefaultAsync(s => s.UbicacionId == r.UbicacionId
            && s.ArticuloId == r.ArticuloId && s.ProductoAwVarianteId == null && s.Lote == null, ct);
        if (saldo is null || saldo.CantidadDisponible < r.Cantidad)
            throw new BusinessRuleException("ROTURA_SALDO_INSUFICIENTE",
                $"Saldo insuficiente: hay {saldo?.CantidadDisponible ?? 0:0.####} y la rotura es de {r.Cantidad:0.####}.");

        var motivo = $"Rotura · {SalidaRotura.Motivos[r.MotivoCodigo]}{(r.Comentario is null ? "" : $": {r.Comentario}")}";
        var mov = await MovimientosRevision.AgregarAsync(db, r.EmpresaId, usuarioId, TipoMovimiento.SalidaPorRotura, r.Fecha,
            motivo.Length > 500 ? motivo[..500] : motivo,
            [new LineaRevisionMov(r.ArticuloId, r.UnidadMedida, r.Cantidad, saldo.CostoPromedioMxn, r.UbicacionId, CentroCostoId: r.CentroCostoId)], ct);
        r.Autorizar(usuarioId, mov.Id, mov.Folio, saldo.CostoPromedioMxn);
        var (almacenId, _) = await AlmacenSucursalResolver.ResolverAsync(db, r.SubAlmacenId, ct);
        await events.PublishAsync(SalidaRoturaRegistradaIntegrationEvent.De(r, almacenId), ct);
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
    }

    /// <summary>Periodo abierto, sub-almacén sin inventario anual en curso y que no sea material en revisión. Devuelve la sucursal.</summary>
    private static async Task<Guid> ValidarReglasDeSalidaAsync(AlmacenDbContext db, IPeriodoContableReadPort periodo, Guid empresaId,
        Guid subAlmacenId, DateOnly fecha, CancellationToken ct)
    {
        if (await db.SubAlmacenes.AsNoTracking().AnyAsync(s => s.Id == subAlmacenId && s.Tipo == TipoSubAlmacen.MaterialEnRevision, ct))
            throw new BusinessRuleException("ROTURA_DESDE_REVISION",
                "El material en revisión no se da de baja aquí: usa la baja por daño del dictamen de calidad.");
        var (_, sucursalId) = await AlmacenSucursalResolver.ResolverAsync(db, subAlmacenId, ct);
        if (await db.Set<BloqueoInventario>().AsNoTracking().AnyAsync(b => b.SubAlmacenId == subAlmacenId && b.Activo && b.BloqueaSalidas, ct))
            throw new BusinessRuleException("SALIDA_BLOQUEADA_POR_INVENTARIO_ANUAL",
                "El sub-almacén está en conteo anual; las salidas están bloqueadas hasta que se aplique o rechace.");
        await PeriodoCerradoValidator.LanzarSiCerradoAsync(db, empresaId, fecha, periodo, ct);
        return sucursalId ?? throw new BusinessRuleException("ROTURA_SUCURSAL_REQUERIDA", "Configura la sucursal del almacén antes de registrar la rotura.");
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IRecepcionesOcReadPort"/>: entradas <c>EntradaCompra</c>
/// Registradas y devoluciones 8.B (Registrada / ConciliadaConNcFiscal) de las OC pedidas.
///
/// <para>
/// La devolución se liga a la línea de OC por su línea de recepción de origen
/// (<c>LineaRecepcionOrigenId</c> → <c>LineaOcId</c>), igual que
/// <c>RecibidoPorLineaOc</c>; la fecha es la de su movimiento de salida. Almacén
/// guarda cantidades en unidad base: se convierten a la unidad de la línea de OC.
/// </para>
///
/// <para>Bypass de empresa, igual que <see cref="AlmacenEntregasReadAdapter"/>.</para>
/// </summary>
public sealed class AlmacenRecepcionesOcReadAdapter(
    AlmacenDbContext db,
    ComprasDbContext compras,
    IConversionUnidadPort conversion,
    ICurrentEmpresaContext empresaContext) : IRecepcionesOcReadPort
{
    public async Task<IReadOnlyList<RecepcionOcLinea>> ListarPorOcAsync(
        IReadOnlyCollection<Guid> ocIds,
        CancellationToken ct)
    {
        if (ocIds.Count == 0)
        {
            return [];
        }

        using var bypass = empresaContext.Bypass();
        var ids = ocIds.Distinct().ToArray();

        var entradas = await db.Movimientos.AsNoTracking()
            .Where(m => m.Estado == EstadoMovimiento.Registrado
                     && m.Tipo == TipoMovimiento.EntradaCompra
                     && m.OcId != null
                     && ids.Contains(m.OcId.Value))
            .SelectMany(m => m.Lineas, (m, l) => new { m, l })
            .Where(x => x.l.LineaOcId != null)
            .Select(x => new Fila(x.m.Id, x.m.Folio, x.m.OcId!.Value, x.l.LineaOcId!.Value,
                x.m.FechaMovimiento, x.l.Cantidad, false))
            .ToListAsync(ct);

        var devoluciones = await (
            from d in db.DevolucionesProveedor.AsNoTracking()
            where d.OrdenCompraOrigenId != null && ids.Contains(d.OrdenCompraOrigenId.Value)
                && d.MovimientoSalidaId != null
                && (d.Estado == EstadoDevolucionProveedor.Registrada
                    || d.Estado == EstadoDevolucionProveedor.ConciliadaConNcFiscal)
            join m in db.Movimientos.AsNoTracking() on d.MovimientoSalidaId equals (Guid?)m.Id
            join ld in db.LineasDevolucionProveedor.AsNoTracking() on d.Id equals ld.DevolucionId
            join lm in db.LineasMovimiento.AsNoTracking() on ld.LineaRecepcionOrigenId equals (Guid?)lm.Id
            where lm.LineaOcId != null
            select new Fila(m.Id, m.Folio, d.OrdenCompraOrigenId!.Value, lm.LineaOcId!.Value,
                m.FechaMovimiento, ld.Cantidad, true)
        ).ToListAsync(ct);

        var filas = entradas.Concat(devoluciones).ToList();
        var lineasIds = filas.Select(f => f.LineaOcId).Distinct().ToArray();
        var lineasOc = await compras.LineasOrdenCompra.AsNoTracking()
            .Where(l => lineasIds.Contains(l.Id))
            .Select(l => new { l.Id, l.ArticuloId, l.UnidadMedida })
            .ToListAsync(ct);
        var factor = new Dictionary<Guid, decimal>();
        foreach (var l in lineasOc)
        {
            var f = (await conversion.ConvertirAsync(l.ArticuloId, 1m, null, l.UnidadMedida, ct)).FactorDocumentoABase;
            factor[l.Id] = f == 0m ? 1m : f;
        }

        return filas.Select(f =>
        {
            var cantidad = Math.Round(f.CantidadBase / factor.GetValueOrDefault(f.LineaOcId, 1m), 4);
            return new RecepcionOcLinea(f.MovimientoId, f.Folio, f.OcId, f.LineaOcId, f.Fecha,
                f.EsDevolucion ? -cantidad : cantidad, f.EsDevolucion);
        }).ToList();
    }

    private sealed record Fila(Guid MovimientoId, string? Folio, Guid OcId, Guid LineaOcId,
        DateOnly Fecha, decimal CantidadBase, bool EsDevolucion);
}

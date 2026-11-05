using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IRecepcionesAwReadPort"/>: entradas <c>EntradaCompra</c> Registradas de la OC con lote,
/// rack (clave de la ubicación) y, si la OC es de importación, los documentos capturados al dar entrada. La cantidad de
/// Almacén (unidad base) se convierte a la unidad de la línea de OC. Bypass de empresa, igual que
/// <see cref="AlmacenRecepcionesOcReadAdapter"/>.
/// </summary>
public sealed class AlmacenRecepcionesAwReadAdapter(
    AlmacenDbContext db,
    ComprasDbContext compras,
    IConversionUnidadPort conversion,
    ICurrentEmpresaContext empresaContext) : IRecepcionesAwReadPort
{
    public async Task<IReadOnlyList<RecepcionAw>> ListarPorOcAsync(Guid ocId, CancellationToken ct)
    {
        using var bypass = empresaContext.Bypass();
        var movimientos = await db.Movimientos.AsNoTracking()
            .Where(m => m.OcId == ocId && m.Tipo == TipoMovimiento.EntradaCompra && m.Estado == EstadoMovimiento.Registrado)
            .OrderBy(m => m.FechaMovimiento).ThenBy(m => m.Folio)
            .Select(m => new { m.Id, m.Folio, m.FechaMovimiento }).ToListAsync(ct);
        if (movimientos.Count == 0) return [];

        var ids = movimientos.Select(m => m.Id).ToList();
        var lineas = await (from l in db.LineasMovimiento.AsNoTracking()
                            where ids.Contains(l.MovimientoId) && l.LineaOcId != null
                            join u in db.Ubicaciones.AsNoTracking() on l.UbicacionId equals (Guid?)u.Id into ubs
                            from u in ubs.DefaultIfEmpty()
                            select new { l.MovimientoId, l.ArticuloId, LineaOcId = l.LineaOcId!.Value, l.Cantidad, l.Lote, Rack = u != null ? u.Clave : null })
            .ToListAsync(ct);
        var lineaIds = lineas.Select(x => x.LineaOcId).Distinct().ToArray();
        var unidades = await compras.LineasOrdenCompra.AsNoTracking()
            .Where(l => lineaIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.UnidadMedida, ct);
        var importaciones = await db.RecepcionesImportacion.AsNoTracking().Include(i => i.Partidas)
            .Where(i => i.OrdenCompraId == ocId).ToListAsync(ct);

        var resultado = new List<RecepcionAw>();
        foreach (var m in movimientos)
        {
            var imp = importaciones.FirstOrDefault(i => i.RecepcionId == m.Id);
            var filas = new List<RecepcionAwLinea>();
            foreach (var l in lineas.Where(x => x.MovimientoId == m.Id))
            {
                var unidad = unidades.GetValueOrDefault(l.LineaOcId, "");
                var factor = (await conversion.ConvertirAsync(l.ArticuloId, 1m, null, unidad, ct)).FactorDocumentoABase;
                filas.Add(new RecepcionAwLinea(l.LineaOcId, Math.Round(l.Cantidad / (factor == 0m ? 1m : factor), 4), l.Lote, l.Rack,
                    imp?.Partidas.FirstOrDefault(p => p.LineaOcId == l.LineaOcId)?.FraccionArancelaria));
            }
            resultado.Add(new RecepcionAw(m.Id, m.Folio, m.FechaMovimiento,
                imp is null ? null : new RecepcionAwImportacion(imp.NumeroPedimento, imp.NumeroBl, imp.NumeroContenedor, imp.Aduana, imp.ClavePedimento),
                filas));
        }
        return resultado;
    }
}

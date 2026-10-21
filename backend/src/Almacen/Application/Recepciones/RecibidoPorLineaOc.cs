using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.Recepciones;

/// <summary>
/// Recibido propio de Almacén por línea de OC (F5, 86e3pc50v). Suma las entradas
/// registradas contra la línea y resta las devoluciones a proveedor que la
/// cancelan, para que el guard de recepción vea lo que Almacén ya recibió aunque
/// Compras aún no lo refleje (asíncrono).
/// </summary>
internal static class RecibidoPorLineaOc
{
    public static async Task<IReadOnlyDictionary<Guid, decimal>> CalcularAsync(
        AlmacenDbContext db, IConversionUnidadPort conversion, OcLectura? oc, CancellationToken ct)
    {
        if (oc is null)
            return new Dictionary<Guid, decimal>();

        var entradas = await db.Movimientos.AsNoTracking()
            .Where(m => m.OcId == oc.Id && m.Tipo == TipoMovimiento.EntradaCompra && m.Estado == EstadoMovimiento.Registrado)
            .SelectMany(m => m.Lineas)
            .Where(l => l.LineaOcId != null)
            .GroupBy(l => l.LineaOcId!.Value)
            .Select(g => new { LineaOcId = g.Key, Cantidad = g.Sum(l => l.Cantidad) })
            .ToListAsync(ct);

        // Devoluciones Registrada/ConciliadaConNcFiscal reducen lo recibido; Rechazada no cuenta.
        var devueltas = await (
            from d in db.DevolucionesProveedor.AsNoTracking()
            where d.OrdenCompraOrigenId == oc.Id
                && (d.Estado == EstadoDevolucionProveedor.Registrada || d.Estado == EstadoDevolucionProveedor.ConciliadaConNcFiscal)
            join ld in db.LineasDevolucionProveedor.AsNoTracking() on d.Id equals ld.DevolucionId
            join lm in db.LineasMovimiento.AsNoTracking() on ld.LineaRecepcionOrigenId equals (Guid?)lm.Id
            where lm.LineaOcId != null
            group ld.Cantidad by lm.LineaOcId!.Value into g
            select new { LineaOcId = g.Key, Cantidad = g.Sum() }
        ).ToListAsync(ct);

        var activos = await db.RecepcionesActivos.Where(x => x.OrdenCompraId == oc.Id).GroupBy(x => x.LineaOcId)
            .Select(g => new { Id = g.Key, Cantidad = g.Sum(x => x.Cantidad) }).ToDictionaryAsync(x => x.Id, x => x.Cantidad, ct);
        var recibido = new Dictionary<Guid, decimal>();
        foreach (var linea in oc.Lineas)
        {
            if (linea.EsActivoFijo) { recibido[linea.LineaId] = activos.GetValueOrDefault(linea.LineaId); continue; }
            var neto = (entradas.FirstOrDefault(e => e.LineaOcId == linea.LineaId)?.Cantidad ?? 0m)
                     - (devueltas.FirstOrDefault(d => d.LineaOcId == linea.LineaId)?.Cantidad ?? 0m);
            if (neto <= 0m)
            {
                recibido[linea.LineaId] = 0m;
                continue;
            }
            // El neto está en unidad base del inventario; se lleva a la unidad de la OC.
            var factor = await FactorDocumentoABaseAsync(conversion, linea.ArticuloId, linea.UnidadMedida, ct);
            recibido[linea.LineaId] = Math.Round(neto / factor, 4);
        }
        return recibido;
    }

    public static async Task<decimal> FactorDocumentoABaseAsync(
        IConversionUnidadPort conversion, Guid articuloId, string unidadDocumento, CancellationToken ct)
        => (await conversion.ConvertirAsync(articuloId, 1m, null, unidadDocumento, ct)).FactorDocumentoABase;
}

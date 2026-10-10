using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>Alcance persistido: todos los bins y documentos de origen, sin omitir sucursales ajenas.</summary>
public sealed class AlmacenSucursalReadAdapter(AlmacenDbContext db, IComprasSucursalReadPort compras,
    ICxpSucursalReadPort cxp) : IAlmacenSucursalReadPort
{
    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        var almacenes = await db.Almacenes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.SucursalId, ct);
        if (tipo == "almacen")
            return almacenes.Select(x => new DocumentoSucursales(x.Key, [x.Value])).ToArray();
        var subs = await db.SubAlmacenes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.AlmacenId, ct);
        IReadOnlyList<Guid> DelSub(Guid sub) => subs.TryGetValue(sub, out var almacen) && almacenes.TryGetValue(almacen, out var sucursal)
            ? [sucursal] : [];
        if (tipo == "reorden")
        {
            var configs = await db.ConfiguracionesReorden.AsNoTracking().Select(x => new { x.Id, x.Nivel, x.EntidadId }).ToListAsync(ct);
            return configs.Select(x => new DocumentoSucursales(x.Id, x.Nivel == NivelReorden.Sucursal ? [x.EntidadId]
                : almacenes.TryGetValue(x.EntidadId, out var sucursal) ? [sucursal] : [])).ToArray();
        }
        var bins = await db.Ubicaciones.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.SubAlmacenId, ct);
        if (tipo == "ubicacion_almacen")
            return bins.Select(x => new DocumentoSucursales(x.Key, DelSub(x.Value))).ToArray();

        var movimientos = await db.Movimientos.AsNoTracking()
            .Where(x => tipo == "recepcion" ? x.Tipo == TipoMovimiento.EntradaCompra
                : x.Tipo == TipoMovimiento.SalidaConsumo || x.Tipo == TipoMovimiento.SalidaPorVale)
            .Select(x => new { x.Id, x.OcId, x.RqId, x.RqRegularizadoraId, x.FacturaId, x.CfdiRecibidoId,
                Bins = x.Lineas.Select(l => l.UbicacionId).ToArray() }).ToListAsync(ct);
        var ocs = (await compras.ListarAsync("orden_compra", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
        var rqs = (await compras.ListarAsync("requisicion", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
        var facturas = (await cxp.ListarAsync("factura_proveedor", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
        var cfdis = (await cxp.ListarAsync("cfdi_recibido", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
        return movimientos.Select(x =>
        {
            var origenes = x.Bins.Select(bin => bin is Guid binId && bins.TryGetValue(binId, out var sub) ? DelSub(sub) : []).ToList();
            if (x.OcId is Guid oc) origenes.Add(ocs.GetValueOrDefault(oc) ?? []);
            if (x.RqId is Guid rq) origenes.Add(rqs.GetValueOrDefault(rq) ?? []);
            if (x.RqRegularizadoraId is Guid regularizadora) origenes.Add(rqs.GetValueOrDefault(regularizadora) ?? []);
            if (x.FacturaId is Guid factura) origenes.Add(facturas.GetValueOrDefault(factura) ?? []);
            if (x.CfdiRecibidoId is Guid cfdi) origenes.Add(cfdis.GetValueOrDefault(cfdi) ?? []);
            // Un origen roto o un movimiento sin líneas no demuestra alcance territorial.
            return new DocumentoSucursales(x.Id, x.Bins.Length == 0 || origenes.Any(o => o.Count == 0)
                ? [] : origenes.SelectMany(o => o).Distinct().ToArray());
        }).ToArray();
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.CuentasPorPagar.Infrastructure;
public sealed class CxpSucursalReadAdapter(CuentasPorPagarDbContext db, IComprasSucursalReadPort compras) : ICxpSucursalReadPort
{
    public async Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
    {
        if (tipo is "cfdi_recibido" or "movimiento_tc" or "estado_cuenta_tc")
        {
            var facturas = await db.FacturasProveedor.AsNoTracking()
                .Select(x => new { x.Id, x.SucursalId, x.CfdiRecibidoId }).ToListAsync(ct);
            var mapaFacturas = facturas.ToDictionary(x => x.Id, x => (IReadOnlyList<Guid>)new[] { x.SucursalId });
            if (tipo == "cfdi_recibido")
            {
                var origenes = facturas.Where(x => x.CfdiRecibidoId != null)
                    .Select(x => new DocumentoSucursales(x.CfdiRecibidoId!.Value, mapaFacturas[x.Id])).ToList();
                var sucursalesNotas = (await ListarAsync("nota_credito_proveedor", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
                var notas = await db.NotasCreditoProveedor.AsNoTracking().Where(x => x.CfdiRecibidoId != null)
                    .Select(x => new { x.Id, x.CfdiRecibidoId }).ToListAsync(ct);
                origenes.AddRange(notas.Select(x => new DocumentoSucursales(x.CfdiRecibidoId!.Value,
                    sucursalesNotas.GetValueOrDefault(x.Id) ?? Array.Empty<Guid>())));
                var ordenes = (await compras.ListarAsync("orden_compra", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
                var anticipos = await db.AnticiposProveedor.AsNoTracking().Where(x => x.CfdiRecibidoId != null)
                    .Select(x => new { x.CfdiRecibidoId, x.OrdenCompraId }).ToListAsync(ct);
                origenes.AddRange(anticipos.Select(x => new DocumentoSucursales(x.CfdiRecibidoId!.Value,
                    x.OrdenCompraId is Guid orden ? ordenes.GetValueOrDefault(orden) ?? Array.Empty<Guid>() : Array.Empty<Guid>())));
                var mapa = origenes.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Any(o => o.Sucursales.Count == 0)
                    ? (IReadOnlyList<Guid>)Array.Empty<Guid>() : x.SelectMany(o => o.Sucursales).Distinct().ToArray());
                var cfdis = await db.CfdisRecibidos.AsNoTracking().Select(x => x.Id).ToListAsync(ct);
                return cfdis.Select(x => new DocumentoSucursales(x, mapa.GetValueOrDefault(x) ?? Array.Empty<Guid>())).ToArray();
            }
            var movimientos = await db.MovimientosTarjetaCredito.AsNoTracking()
                .Select(x => new { x.Id, x.FacturaProveedorId, x.MovimientoOriginalId, x.EstadoCuentaTcId }).ToListAsync(ct);
            var mapaMovimientos = new Dictionary<Guid, IReadOnlyList<Guid>>();
            IReadOnlyList<Guid> ResolverMovimiento(Guid id, HashSet<Guid> vistos)
            {
                if (!vistos.Add(id)) return Array.Empty<Guid>();
                var movimiento = movimientos.FirstOrDefault(x => x.Id == id);
                return movimiento?.FacturaProveedorId is Guid factura ? mapaFacturas.GetValueOrDefault(factura) ?? Array.Empty<Guid>() :
                    movimiento?.MovimientoOriginalId is Guid original ? ResolverMovimiento(original, vistos) : Array.Empty<Guid>();
            }
            foreach (var movimiento in movimientos) mapaMovimientos[movimiento.Id] = ResolverMovimiento(movimiento.Id, []);
            if (tipo == "movimiento_tc") return mapaMovimientos.Select(x => new DocumentoSucursales(x.Key, x.Value)).ToArray();
            var estados = await db.EstadosCuentaTc.AsNoTracking().Select(x => new { x.Id, x.FacturaProveedorId }).ToListAsync(ct);
            return estados.Select(x => {
                var origenes = movimientos.Where(m => m.EstadoCuentaTcId == x.Id).Select(m => mapaMovimientos[m.Id]).ToList();
                if (x.FacturaProveedorId is Guid factura) origenes.Add(mapaFacturas.GetValueOrDefault(factura) ?? Array.Empty<Guid>());
                return new DocumentoSucursales(x.Id, origenes.Count == 0 || origenes.Any(o => o.Count == 0)
                    ? Array.Empty<Guid>() : origenes.SelectMany(o => o).Distinct().ToArray());
            }).ToArray();
        }
        if (tipo == "anticipo_proveedor")
        {
            var ordenes = (await compras.ListarAsync("orden_compra", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
            var anticipos = await db.AnticiposProveedor.AsNoTracking().Select(x => new { x.Id, x.OrdenCompraId }).ToListAsync(ct);
            return anticipos.Select(x => new DocumentoSucursales(x.Id,
                x.OrdenCompraId is Guid id ? ordenes.GetValueOrDefault(id) ?? Array.Empty<Guid>() : Array.Empty<Guid>())).ToArray();
        }
        if (tipo == "nota_credito_proveedor")
        {
            var facturas = await db.FacturasProveedor.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToDictionaryAsync(x => x.Id, x => x.SucursalId, ct);
            var anticipos = (await ListarAsync("anticipo_proveedor", ct)).ToDictionary(x => x.Id, x => x.Sucursales);
            var notas = await db.NotasCreditoProveedor.AsNoTracking().Select(x => new { x.Id, x.FacturaOrigenId, x.AnticipoOrigenId }).ToListAsync(ct);
            return notas.Select(x => new DocumentoSucursales(x.Id,
                x.FacturaOrigenId is Guid id && facturas.TryGetValue(id, out var sucursal) ? new[] { sucursal } :
                x.AnticipoOrigenId is Guid anticipo ? anticipos.GetValueOrDefault(anticipo) ?? Array.Empty<Guid>() : Array.Empty<Guid>())).ToArray();
        }
        if (tipo == "comprobacion")
            return await db.ComprobacionesGastos.AsNoTracking().Select(x => new DocumentoSucursales(x.Id, new[] { x.SucursalId })).ToListAsync(ct);
        if (tipo == "reposicion_caja")
            return await db.ReposicionesCajaChica.AsNoTracking().Select(x => new DocumentoSucursales(x.Id, new[] { x.SucursalId })).ToListAsync(ct);
        if (tipo == "nota_cargo")
        {
            var notas = await db.NotasCargo.AsNoTracking().Select(x => new { x.Id, x.SucursalId, x.FacturaOrigenId }).ToListAsync(ct);
            var facturas = await db.FacturasProveedor.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToDictionaryAsync(x => x.Id, x => x.SucursalId, ct);
            return notas.Select(x => new DocumentoSucursales(x.Id,
                x.SucursalId is Guid id ? new[] { id } :
                x.FacturaOrigenId is Guid f && facturas.TryGetValue(f, out var sucursal) ? new[] { sucursal } : Array.Empty<Guid>())).ToArray();
        }
        var filas = await db.FacturasProveedor.AsNoTracking().Select(x => new { x.Id, x.SucursalId }).ToListAsync(ct);
        return filas.Select(x => new DocumentoSucursales(x.Id, new[] { x.SucursalId })).ToArray();
    }
}

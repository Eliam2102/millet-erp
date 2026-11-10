using Millet.ComercioExterior.Application;
using Millet.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.Ports;
using Millet.ComercioExterior.Domain;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;
public sealed class PedidosEmbarcablesReadAdapter(FacturacionDbContext db) : IPedidosEmbarcablesReadPort
{
    public Task<IReadOnlyList<PedidoEmbarcable>> BuscarPorNumeroAsync(Guid empresaId, IReadOnlyCollection<string> numeros, CancellationToken ct) =>
        LeerAsync(db.PedidosFacturables.Where(p => p.EmpresaId == empresaId && p.Origen == OrigenPedido.Aw && p.NumeroPedido != null && numeros.Contains(p.NumeroPedido)), ct);
    public async Task<IReadOnlyList<PedidoEmbarcable>> ListarAsync(Guid clienteId, Guid sucursalId, CancellationToken ct) =>
        await LeerAsync(db.PedidosFacturables.Where(p => p.ClienteId == clienteId && p.SucursalId == sucursalId &&
            p.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce && p.Estado == EstadoPedidoFacturable.Importado), ct);
    public Task<IReadOnlyList<PedidoEmbarcable>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        LeerAsync(db.PedidosFacturables.Where(p => ids.Contains(p.Id)), ct);
    public Task<bool> TieneFacturaContenedorAsync(Guid contenedorId, CancellationToken ct) =>
        (from f in db.FacturasVenta
         join s in db.SalidasFacturables on f.SalidaFacturableId equals (Guid?)s.Id
         where s.Origen == OrigenSalidaFacturable.ContenedorExportacion && s.OrigenId == contenedorId &&
             f.Estado != EstadoTimbrado.Cancelado && f.Estado != EstadoTimbrado.Descartada
         select f.Id).AnyAsync(ct);

    public async Task<bool> FacturasDespachoAsync(Guid contenedorId, IReadOnlyList<PedidoContenedorEvento> requeridas, DateOnly fecha, CancellationToken ct)
    {
        if (requeridas.Count == 0) return false;
        var salidas = await db.SalidasFacturables.AsNoTracking().Include(s => s.Lineas)
            .Where(s => s.Origen == OrigenSalidaFacturable.ContenedorExportacion && s.OrigenId == contenedorId && s.DescartadaEn == null)
            .ToListAsync(ct);
        var ids = salidas.Select(s => s.Id).ToArray();
        var facturas = await db.FacturasVenta.AsNoTracking()
            .Where(f => f.SalidaFacturableId != null && ids.Contains(f.SalidaFacturableId.Value) &&
                f.Estado == EstadoTimbrado.Timbrado && f.Uuid != null && f.FechaTimbrado != null)
            .Select(f => new { f.SalidaFacturableId, f.FechaTimbrado }).ToListAsync(ct);
        var timbradas = facturas.Where(f => FechaContable.HoyLocal(f.FechaTimbrado!.Value) <= fecha)
            .Select(f => f.SalidaFacturableId!.Value).ToHashSet();
        return requeridas.All(p => salidas.Any(s => s.PedidoFacturableId == p.PedidoFacturableId &&
            timbradas.Contains(s.Id) && s.MismasCantidades(p.Lineas.Select(l => (l.Posicion, l.Cantidad)).ToArray())));
    }
    private async Task<IReadOnlyList<PedidoEmbarcable>> LeerAsync(IQueryable<PedidoFacturable> q, CancellationToken ct)
    {
        var pedidos = await q.AsNoTracking().Include(p => p.Lineas).OrderBy(p => p.NumeroPedido).ToListAsync(ct);
        var facturasIds = pedidos.Where(p => p.ComprobanteVigenteId != null).Select(p => p.ComprobanteVigenteId!.Value).ToArray();
        var facturas = await db.FacturasVenta.AsNoTracking().Where(f => facturasIds.Contains(f.Id) && f.Estado != EstadoTimbrado.Cancelado && f.Estado != EstadoTimbrado.Descartada)
            .Select(f => new { f.Id, f.Folio, f.Uuid }).ToDictionaryAsync(f => f.Id, ct);
        return pedidos.Select(p => new PedidoEmbarcable(p.Id, p.NumeroPedido ?? "[PEDIDO]", p.ClienteId, p.ClienteNombre,
            p.SucursalId, p.ComportamientoFiscal == ComportamientoFiscal.ExportacionConCce, p.Estado.ToString(), p.VersionOrigen,
            p.Lineas.OrderBy(l => l.Posicion).Select(l => new LineaEmbarcable(l.Posicion, l.ProductoId, l.ProductoDescripcion, l.Cantidad, l.ClaveUnidadSat ?? "")).ToArray(),
            p.ComprobanteVigenteId is Guid fid ? facturas.GetValueOrDefault(fid)?.Folio : null,
            p.ComprobanteVigenteId is Guid uuid ? facturas.GetValueOrDefault(uuid)?.Uuid : null,
            p.ComprobanteVigenteId is Guid vigente && facturas.ContainsKey(vigente))).ToArray();
    }
}

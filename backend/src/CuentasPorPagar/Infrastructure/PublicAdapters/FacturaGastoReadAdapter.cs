using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
public sealed class FacturaGastoReadAdapter(CuentasPorPagarDbContext db, IPagosFacturaCeReadPort pagos, Millet.CuentasPorPagar.Domain.Ports.Compras.IComprasOcReadPort compras) : IFacturaGastoReadPort
{
    public async Task<IReadOnlyList<FacturaServicioSaldo>> ObtenerSaldosAsync(Guid empresaId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var facturas = await db.FacturasProveedor.AsNoTracking().Where(f => f.EmpresaId == empresaId && ids.Contains(f.Id)).ToArrayAsync(ct);
        var vigentes = await pagos.ImportesVigentesAsync(empresaId, ids, ct);
        var firmas = new Dictionary<Guid, bool>();
        foreach (var ocId in facturas.Where(f => f.EsServicioComercioExterior).Select(f => f.OrdenCompraId).OfType<Guid>().Distinct())
        {
            var oc = await compras.ObtenerAsync(ocId, ct);
            firmas[ocId] = oc is { EsServicioComercioExterior: true, ServicioCeNivel1Autorizado: true, ServicioCeNivel2Autorizado: true }
                && oc.EmpresaId == empresaId && oc.Estado is "Autorizada" or "Cerrada";
        }
        // Una reversa bancaria aún pendiente de proyección en CxP no puede aparentar saldo cero.
        return facturas.Select(f => new FacturaServicioSaldo(f.Id, f.EmpresaId, f.ProveedorId, f.SucursalId,
            f.UuidCfdi ?? f.FolioProveedor ?? f.Id.ToString(), f.SaldoPendiente + Math.Max(0, f.ImportePagado - vigentes.GetValueOrDefault(f.Id)),
            (f.Estado is EstadoPasivo.Autorizada or EstadoPasivo.Pagada)
                && (!f.EsServicioComercioExterior || (f.OrdenCompraId is Guid ocId && firmas.GetValueOrDefault(ocId))),
            vigentes.GetValueOrDefault(f.Id) > f.ImportePagado, f.Moneda)).ToArray();
    }
    public async Task<FacturaGastoLectura?> BuscarAsync(Guid proveedorId, string referencia, Guid? id, CancellationToken ct)
    {
        referencia = referencia.Trim().ToUpperInvariant();
        var factura = await db.Set<FacturaProveedor>().AsNoTracking().Where(f => f.ProveedorId == proveedorId && f.Estado != EstadoPasivo.Cancelada &&
            (id != null ? f.Id == id : f.UuidCfdi == referencia || f.FolioProveedor == referencia)).OrderByDescending(f => f.CreatedAt).FirstOrDefaultAsync(ct);
        var fechaTesoreria = factura is null ? null : await pagos.UltimoPagoAsync(factura.Id, ct);
        return factura is null ? null : new(factura.Id, factura.ProveedorId, factura.SucursalId, factura.Moneda,
            DateOnly.FromDateTime(factura.FechaDocumento.Date), factura.UuidCfdi ?? factura.FolioProveedor ?? referencia, factura.TipoCambio, factura.ImportePagado, factura.SaldoPendiente,
            fechaTesoreria ?? (factura.ImportePagado > 0 ?
            await db.MovimientosPasivo.Where(m => m.FacturaProveedorId == factura.Id && m.Tipo == TipoMovimientoPasivo.Pago && m.Monto > 0).MaxAsync(m => (DateOnly?)m.Fecha, ct) : null));
    }
}

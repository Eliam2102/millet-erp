using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
public sealed class FacturaGastoReadAdapter(CuentasPorPagarDbContext db, IPagosFacturaCeReadPort pagos) : IFacturaGastoReadPort
{
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

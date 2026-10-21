using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Application.Integration;
namespace Millet.Facturacion.Infrastructure;
public sealed class VentaActivoReadAdapter(FacturacionDbContext db) : IVentaActivoReadPort
{
    public async Task<VentaActivoLectura?> ObtenerAsync(Guid factura, CancellationToken ct)
    {
        var f = await db.FacturasVenta.AsNoTracking().SingleOrDefaultAsync(x => x.Id == factura, ct);
        if (f?.AutorizacionId == null || f.FechaTimbrado == null) return null;
        var a = await db.AutorizacionesVentaActivo.AsNoTracking().SingleAsync(x => x.Id == f.AutorizacionId, ct);
        return new(f.EmpresaId, a.ActivoRef, decimal.Round((f.Subtotal - f.Descuento) * (f.TipoCambio ?? 1), 2),
            EventosContablesFacturacion.FechaContable(f, f.FechaTimbrado.Value), f.Estado == EstadoTimbrado.Cancelado);
    }
}

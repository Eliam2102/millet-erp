using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Application.Integration;
namespace Millet.Facturacion.Infrastructure;
public sealed class VentaActivoReadAdapter(FacturacionDbContext db) : IVentaActivoReadPort, IDisposicionVentaReadPort
{
    public Task<bool> TieneAbiertaAsync(string activoRef, CancellationToken ct) => db.AutorizacionesVentaActivo.AnyAsync(x => x.ActivoRef == activoRef && (x.Estado == Millet.Facturacion.Domain.Activos.EstadoAutorizacionActivo.Autorizada || x.Estado == Millet.Facturacion.Domain.Activos.EstadoAutorizacionActivo.Usada && db.FacturasVenta.Any(f => f.Id == x.FacturaVentaId && f.Estado != EstadoTimbrado.Cancelado)), ct);
    public async Task<IReadOnlyList<AutorizacionVentaHistorial>> HistorialAsync(string activoRef, CancellationToken ct) =>
        (await db.AutorizacionesVentaActivo.AsNoTracking().Where(x => x.ActivoRef == activoRef).OrderByDescending(x => x.FechaAutorizacion).ToListAsync(ct))
            .Select(x => new AutorizacionVentaHistorial(x.Id, x.Version, x.Estado.ToString(), x.MotivoRechazo, x.AutorizadoPor, x.FechaAutorizacion, x.RechazadoEn)).ToList();
    public async Task<VentaActivoLectura?> ObtenerAsync(Guid factura, CancellationToken ct)
    {
        var f = await db.FacturasVenta.AsNoTracking().SingleOrDefaultAsync(x => x.Id == factura, ct);
        if (f?.AutorizacionId == null || f.FechaTimbrado == null) return null;
        var a = await db.AutorizacionesVentaActivo.AsNoTracking().SingleAsync(x => x.Id == f.AutorizacionId, ct);
        return new(f.EmpresaId, a.ActivoRef, decimal.Round((f.Subtotal - f.Descuento) * (f.TipoCambio ?? 1), 2),
            EventosContablesFacturacion.FechaContable(f, f.FechaTimbrado.Value), f.Estado == EstadoTimbrado.Cancelado);
    }
}

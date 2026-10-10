using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Application.PublicPorts;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Infrastructure.PublicAdapters;

public sealed class PagosTrazabilidadReadAdapter(TesoreriaDbContext db) : IPagosTrazabilidadReadPort
{
    public async Task<IReadOnlyList<PagoTrazabilidad>> ListarAsync(Guid? facturaId, Guid? pagoId, CancellationToken ct)
    {
        var filas = await (from p in db.AplicacionesPagoProveedor.AsNoTracking()
            join m in db.MovimientosBancarios.AsNoTracking() on p.MovimientoId equals m.Id
            where (facturaId != null && p.FacturaProveedorId == facturaId) || (pagoId != null && p.Id == pagoId)
            select new { p.Id, p.FacturaProveedorId, m.ReferenciaBancaria, p.Revertida, p.CreadoEn }).ToListAsync(ct);
        return filas.Select(p => new PagoTrazabilidad(p.Id, p.FacturaProveedorId,
            p.ReferenciaBancaria ?? $"Pago {p.Id.ToString()[..8]}", p.Revertida ? "Revertido" : "Aplicado", p.CreadoEn)).ToList();
    }
}

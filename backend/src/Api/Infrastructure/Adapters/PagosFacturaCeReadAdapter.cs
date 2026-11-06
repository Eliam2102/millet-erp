using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.PublicPorts;
using Millet.Tesoreria.Infrastructure.Persistence;
namespace Millet.Api.Infrastructure.Adapters;
public sealed class PagosFacturaCeReadAdapter(TesoreriaDbContext db) : IPagosFacturaCeReadPort
{
    public Task<DateOnly?> UltimoPagoAsync(Guid facturaId, CancellationToken ct)
        => (from a in db.AplicacionesPagoProveedor.AsNoTracking()
            join m in db.MovimientosBancarios.AsNoTracking() on a.MovimientoId equals m.Id
            where a.FacturaProveedorId == facturaId && !a.Revertida
            select (DateOnly?)m.FechaValor).MaxAsync(ct);
}

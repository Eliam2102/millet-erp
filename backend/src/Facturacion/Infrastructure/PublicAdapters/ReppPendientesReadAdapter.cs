using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

public sealed class ReppPendientesReadAdapter(FacturacionDbContext db) : IReppPendientesReadPort
{
    public async Task<ReppClienteEstado> ConsultarAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        var pagos = await db.ReppPendientes.AsNoTracking().Where(p => p.ClienteId == clienteId)
            .Select(p => new { p.Estado, p.ReciboPagoId }).ToListAsync(cancellationToken);
        return new(pagos.Any(p => p.Estado == EstadoReppPendiente.Pendiente),
            pagos.Where(p => p.Estado == EstadoReppPendiente.Emitido && p.ReciboPagoId.HasValue)
                .Select(p => p.ReciboPagoId!.Value).ToArray());
    }
}

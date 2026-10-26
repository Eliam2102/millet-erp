using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Infrastructure.PublicAdapters;

public sealed class ReppPendientesReadAdapter(FacturacionDbContext db) : IReppPendientesReadPort
{
    public async Task<ReppClienteEstado> ConsultarAsync(Guid clienteId, CancellationToken cancellationToken)
    {
        var pagos = await db.ReppPendientes.AsNoTracking()
            .Where(p => p.ClienteId == clienteId && p.Estado != EstadoReppPendiente.Descartado)
            .OrderBy(p => p.FechaValor)
            .Select(p => new { p.Id, p.Estado, p.FechaValor, p.FechaLimite, p.Monto, p.Moneda, p.Referencia, p.ReciboPagoId })
            .ToListAsync(cancellationToken);
        var detalle = pagos
            .Where(p => p.Estado == EstadoReppPendiente.Pendiente
                || (p.Estado == EstadoReppPendiente.Emitido && p.ReciboPagoId.HasValue))
            .Select(p => new ReppPendienteResumen(p.Id, p.Estado.ToString(), p.FechaValor, p.FechaLimite,
                p.Monto, p.Moneda, p.Referencia, p.ReciboPagoId))
            .ToArray();
        return new(pagos.Any(p => p.Estado == EstadoReppPendiente.Pendiente),
            pagos.Where(p => p.Estado == EstadoReppPendiente.Emitido && p.ReciboPagoId.HasValue)
                .Select(p => p.ReciboPagoId!.Value).ToArray(),
            detalle);
    }
}

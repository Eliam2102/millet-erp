using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.CuentasPorCobrar;

public sealed class PlazoCreditoClienteReadAdapter(CuentasPorCobrarDbContext db) : IPlazoCreditoClienteReadPort
{
    public Task<int?> ObtenerPlazoDiasAsync(Guid clienteId, string moneda, CancellationToken ct) =>
        db.LineasCredito.AsNoTracking()
            .Where(l => l.ClienteId == clienteId && l.Moneda == moneda
                && (l.Estado == EstadoLineaCredito.Activa || l.Estado == EstadoLineaCredito.Bloqueada))
            // Si conviven una línea bloqueada vieja y una activa, manda la activa.
            .OrderBy(l => l.Estado)
            .Select(l => (int?)l.PlazoDias)
            .FirstOrDefaultAsync(ct);
}

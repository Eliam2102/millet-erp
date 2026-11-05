using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Ports;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Infrastructure.Adapters;

/// <summary>Implementa <see cref="IPendientesMaestroReadPort"/> con las facturas del cliente que aún tienen saldo por cobrar.</summary>
public sealed class SaldoClientePendienteReadAdapter(CuentasPorCobrarDbContext db, ICurrentEmpresaContext empresa)
    : IPendientesMaestroReadPort
{
    public async Task<IReadOnlyList<string>> ObtenerAsync(TipoMaestro tipo, Guid id, CancellationToken cancellationToken)
    {
        if (tipo != TipoMaestro.Cliente) return [];
        using var bypass = empresa.Bypass();
        var facturas = await db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId == id && f.Estado != EstadoFacturaCartera.Cancelada && f.Estado != EstadoFacturaCartera.Pagada
                        && f.Total - f.MontoPagado - f.MontoNc > 0)
            .Select(f => new { f.Folio, Saldo = f.Total - f.MontoPagado - f.MontoNc, f.Moneda })
            .ToListAsync(cancellationToken);
        return facturas.Select(f => $"Factura {f.Folio} con saldo por cobrar de {f.Saldo:N2} {f.Moneda}.").ToList();
    }
}

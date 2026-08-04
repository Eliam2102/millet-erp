using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.Cartera;

// ============================================================================
// CXC-PR3: saldo neto por cliente (Decisión 13-K del levantamiento §3.2):
//   saldo_neto = facturado − pagos_aplicados − NC
// derivado 100% de la proyección factura_cartera, por moneda (nunca se
// convierte). El reporte completo ADR-0036 (antigüedad + estado de cuenta)
// entra en CXC-PR6.
// ============================================================================

public sealed record SaldoNetoMonedaResponse(
    string Moneda,
    decimal TotalFacturado,
    decimal MontoPagado,
    decimal MontoNc,
    decimal SaldoNeto,
    int FacturasAbiertas);

public sealed record SaldoNetoClienteResponse(
    Guid ClienteId,
    IReadOnlyList<SaldoNetoMonedaResponse> Monedas);

public sealed record SaldoNetoClienteQuery(Guid ClienteId) : IRequest<SaldoNetoClienteResponse>;

public sealed class SaldoNetoClienteHandler : IRequestHandler<SaldoNetoClienteQuery, SaldoNetoClienteResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public SaldoNetoClienteHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<SaldoNetoClienteResponse> Handle(
        SaldoNetoClienteQuery query, CancellationToken cancellationToken)
    {
        var monedas = await _db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId == query.ClienteId
                     && f.Estado != EstadoFacturaCartera.Cancelada)
            .GroupBy(f => f.Moneda)
            .Select(g => new SaldoNetoMonedaResponse(
                g.Key,
                g.Sum(f => f.Total),
                g.Sum(f => f.MontoPagado),
                g.Sum(f => f.MontoNc),
                g.Sum(f => f.Total - f.MontoPagado - f.MontoNc),
                g.Count(f => f.Estado == EstadoFacturaCartera.Abierta
                          || f.Estado == EstadoFacturaCartera.Parcial)))
            .ToListAsync(cancellationToken);

        return new SaldoNetoClienteResponse(query.ClienteId, monedas);
    }
}

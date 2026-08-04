using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.CreditoDisponible;

// ============================================================================
// CXC-PR2/PR3: crédito disponible por cliente (§3.2 del 00-levantamiento):
//   disponible = limite − facturado − liberado_sin_factura
// por cada línea del cliente (el cálculo es por moneda — nunca se convierte).
//
// Desde CXC-PR3 `facturado` es REAL: saldo pendiente (total − pagado − NC)
// de las facturas Abierta/Parcial del cliente en la moneda de la línea,
// derivado de la proyección factura_cartera.
//
// `liberado_sin_factura` sigue provisional (= 0) hasta que A+W cierre la
// definición del gap G1; por eso `datoIncompleto` permanece en true.
// ============================================================================

public sealed record CreditoDisponibleLineaResponse(
    Guid LineaCreditoId,
    string Moneda,
    decimal Limite,
    decimal Facturado,
    decimal LiberadoSinFactura,
    decimal Disponible,
    EstadoLineaCredito Estado,
    OrigenLineaCredito Origen,
    int PlazoDias);

public sealed record CreditoDisponibleResponse(
    Guid ClienteId,
    IReadOnlyList<CreditoDisponibleLineaResponse> Lineas,
    bool DatoIncompleto);

public sealed record CreditoDisponibleQuery(Guid ClienteId) : IRequest<CreditoDisponibleResponse>;

public sealed class CreditoDisponibleHandler : IRequestHandler<CreditoDisponibleQuery, CreditoDisponibleResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public CreditoDisponibleHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<CreditoDisponibleResponse> Handle(
        CreditoDisponibleQuery query, CancellationToken cancellationToken)
    {
        var lineas = await _db.LineasCredito.AsNoTracking()
            .Where(l => l.ClienteId == query.ClienteId)
            .OrderBy(l => l.Moneda).ThenBy(l => l.Estado)
            .ToListAsync(cancellationToken);

        // CXC-PR3: facturado real = saldo pendiente de facturas Abierta/Parcial
        // del cliente, agrupado por moneda desde la proyección factura_cartera.
        var facturadoPorMoneda = await _db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId == query.ClienteId
                     && (f.Estado == EstadoFacturaCartera.Abierta
                      || f.Estado == EstadoFacturaCartera.Parcial))
            .GroupBy(f => f.Moneda)
            .Select(g => new { Moneda = g.Key, Saldo = g.Sum(f => f.Total - f.MontoPagado - f.MontoNc) })
            .ToDictionaryAsync(x => x.Moneda, x => x.Saldo, cancellationToken);

        var respuesta = lineas.Select(l =>
        {
            var facturado = facturadoPorMoneda.GetValueOrDefault(l.Moneda, 0m);

            // PLATFORM-TODO(<CreditoLiberadoSinFactura>): A+W no expone aún la
            // vista de "liberado sin factura / material en firme" (gap G1 del
            // 00-levantamiento). Cuando exista, un adapter de lectura la
            // consume aquí y se quita la bandera DatoIncompleto.
            var liberadoSinFactura = 0m;

            return new CreditoDisponibleLineaResponse(
                LineaCreditoId: l.Id,
                Moneda: l.Moneda,
                Limite: l.Limite,
                Facturado: facturado,
                LiberadoSinFactura: liberadoSinFactura,
                Disponible: l.Limite - facturado - liberadoSinFactura,
                Estado: l.Estado,
                Origen: l.Origen,
                PlazoDias: l.PlazoDias);
        }).ToList();

        return new CreditoDisponibleResponse(
            ClienteId: query.ClienteId,
            Lineas: respuesta,
            DatoIncompleto: true);
    }
}

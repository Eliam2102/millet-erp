using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
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
// C7.1 (86e3kdxx4): `liberado_sin_factura` es REAL — pedidos a crédito en
// firme sin factura vigente, menos su anticipo ligado (puerto de lectura de
// Facturación). Mismo cálculo que la liberación (ComponentesCredito).
// `datoIncompleto` queda en false; se conserva en el contrato por
// compatibilidad.
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
    private readonly IFacturacionPedidosSinFacturarReadPort _pedidos;

    public CreditoDisponibleHandler(CuentasPorCobrarDbContext db, IFacturacionPedidosSinFacturarReadPort pedidos)
    {
        _db = db; _pedidos = pedidos;
    }

    public async Task<CreditoDisponibleResponse> Handle(
        CreditoDisponibleQuery query, CancellationToken cancellationToken)
    {
        var lineas = await _db.LineasCredito.AsNoTracking()
            .Where(l => l.ClienteId == query.ClienteId)
            .OrderBy(l => l.Moneda).ThenBy(l => l.Estado)
            .ToListAsync(cancellationToken);

        var facturadoPorMoneda = await ComponentesCredito.FacturadoPorMonedaAsync(
            _db, query.ClienteId, cancellationToken);
        var liberadoPorMoneda = lineas.Count == 0
            ? []
            : await ComponentesCredito.LiberadoSinFacturaPorMonedaAsync(
                _pedidos, query.ClienteId, pedidoRefExcluido: null, cancellationToken);

        var respuesta = lineas.Select(l =>
        {
            var facturado = facturadoPorMoneda.GetValueOrDefault(l.Moneda, 0m);
            var liberadoSinFactura = liberadoPorMoneda.GetValueOrDefault(l.Moneda, 0m);

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
            DatoIncompleto: false);
    }
}

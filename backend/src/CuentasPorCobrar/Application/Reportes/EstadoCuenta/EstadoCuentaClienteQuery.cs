using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Reportes.Comun;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.Reportes.EstadoCuenta;

/// <summary>
/// Estado de cuenta del cliente (CXC-PR6, ADR-0036, levantamiento §1.4):
/// todos los movimientos aplicados — facturas (cargo), pagos y NCs
/// (abono, desde <c>MovimientoCartera</c> no revertidos) — más los
/// saldos de anticipo vigentes vía <see cref="IFacturacionAnticiposReadPort"/>
/// ("dinero del cliente en la casa"; los anticipos NO son cartera pero
/// sí movimiento del cliente). Todo por moneda, nunca se convierte.
/// </summary>
public sealed record EstadoCuentaClienteQuery(Guid ClienteId) : IRequest<ReporteJsonResponse>;

public sealed class EstadoCuentaClienteHandler : IRequestHandler<EstadoCuentaClienteQuery, ReporteJsonResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly IFacturacionAnticiposReadPort _anticipos;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public EstadoCuentaClienteHandler(
        CuentasPorCobrarDbContext db,
        IFacturacionAnticiposReadPort anticipos,
        IClienteReadPort clientes,
        IClock clock)
    {
        _db = db; _anticipos = anticipos; _clientes = clientes; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        EstadoCuentaClienteQuery query, CancellationToken cancellationToken)
    {
        var facturas = await _db.FacturasCartera.AsNoTracking()
            .Where(f => f.ClienteId == query.ClienteId
                     && f.Estado != EstadoFacturaCartera.Cancelada)
            .OrderBy(f => f.FechaTimbrado)
            .ToListAsync(cancellationToken);

        var facturaIds = facturas.Select(f => f.Id).ToList();
        var movimientos = await _db.MovimientosCartera.AsNoTracking()
            .Where(m => facturaIds.Contains(m.FacturaCarteraId) && !m.Revertido)
            .OrderBy(m => m.FechaMovimiento)
            .ToListAsync(cancellationToken);
        var folioPorFactura = facturas.ToDictionary(f => f.Id, f => (f.Folio, f.Moneda));

        var anticipos = await _anticipos.ListarPorClienteAsync(query.ClienteId, cancellationToken);

        var filas = new List<IReadOnlyDictionary<string, object?>>();

        foreach (var f in facturas)
        {
            filas.Add(new Dictionary<string, object?>
            {
                ["fecha"] = f.FechaTimbrado,
                ["tipo"] = "Factura",
                ["referencia"] = f.Folio,
                ["moneda"] = f.Moneda,
                ["cargo"] = f.Total,
                ["abono"] = null,
                ["saldo_documento"] = f.SaldoPendiente,
                ["estado"] = f.Estado.ToString(),
            });
        }

        foreach (var m in movimientos)
        {
            var (folio, moneda) = folioPorFactura[m.FacturaCarteraId];
            filas.Add(new Dictionary<string, object?>
            {
                ["fecha"] = m.FechaMovimiento,
                ["tipo"] = m.Tipo == TipoMovimientoCartera.Pago ? "Pago" : "Nota de crédito",
                // Folio de la factura afectada; el id interno del comprobante
                // origen (REPP/NC) no le dice nada al usuario (feedback 2026-07-14).
                ["referencia"] = folio,
                ["moneda"] = moneda,
                ["cargo"] = null,
                ["abono"] = m.Importe,
                ["saldo_documento"] = null,
                ["estado"] = null,
            });
        }

        // Anticipos vigentes con saldo — informativos, no cartera (§1.4).
        foreach (var a in anticipos.Where(a => a.SaldoDisponible > 0))
        {
            filas.Add(new Dictionary<string, object?>
            {
                ["fecha"] = null,
                ["tipo"] = "Anticipo (saldo disponible)",
                ["referencia"] = a.PedidoOrigenRef ?? "Anticipo sin pedido",
                ["moneda"] = a.Moneda,
                ["cargo"] = null,
                ["abono"] = a.SaldoDisponible,
                ["saldo_documento"] = a.Saldo,
                ["estado"] = a.Estado,
            });
        }

        var ordenadas = filas
            .OrderBy(f => f["fecha"] is DateTimeOffset d ? d : DateTimeOffset.MaxValue)
            .ToList();

        var totales = new Dictionary<string, object?>
        {
            ["cargo"] = facturas.Sum(f => f.Total),
            ["abono"] = movimientos.Sum(m => m.Importe)
                        + anticipos.Where(a => a.SaldoDisponible > 0).Sum(a => a.SaldoDisponible),
            ["saldo_documento"] = facturas.Sum(f => f.SaldoPendiente),
        };

        // Razón social en vez del UUID interno (feedback 2026-07-14).
        var cliente = await _clientes.ObtenerAsync(query.ClienteId, cancellationToken);

        return new ReporteJsonResponse(
            Titulo: "Estado de cuenta del cliente",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: [new FiltroAplicado("Cliente", cliente?.RazonSocial ?? query.ClienteId.ToString())],
            Columnas:
            [
                new("fecha",           "Fecha",       TipoColumnaReporte.FechaHora, AlineacionColumna.Izquierda),
                new("tipo",            "Movimiento",  TipoColumnaReporte.Texto,     AlineacionColumna.Izquierda),
                new("referencia",      "Referencia",  TipoColumnaReporte.Texto,     AlineacionColumna.Izquierda),
                new("moneda",          "Moneda",      TipoColumnaReporte.Texto,     AlineacionColumna.Centro),
                new("cargo",           "Cargo",       TipoColumnaReporte.Moneda,    AlineacionColumna.Derecha),
                new("abono",           "Abono",       TipoColumnaReporte.Moneda,    AlineacionColumna.Derecha),
                new("saldo_documento", "Saldo doc.",  TipoColumnaReporte.Moneda,    AlineacionColumna.Derecha),
                new("estado",          "Estado",      TipoColumnaReporte.Texto,     AlineacionColumna.Centro),
            ],
            Filas: ordenadas,
            Totales: totales);
    }
}

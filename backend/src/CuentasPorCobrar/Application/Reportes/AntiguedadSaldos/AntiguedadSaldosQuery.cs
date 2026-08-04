using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Reportes.Comun;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Application.Reportes.AntiguedadSaldos;

/// <summary>
/// Antigüedad de saldos de cartera (CXC-PR6, ADR-0036, levantamiento
/// §1.2). Agrupa facturas vivas (Abierta/Parcial) por cliente + moneda —
/// nunca convierte — con columna <c>por_vencer</c> + buckets de días
/// VENCIDOS configurables (<c>CuentasPorCobrar:Reportes:BucketLimites</c>,
/// default 15/30/60/90). "Saldo vencido = suma de los buckets" (§2.2).
/// La clasificación A/B/C/E sale de la línea de crédito activa del
/// (cliente, moneda) — atributo, no fórmula manual.
///
/// <para>Reemplaza el corte semanal de 45-90 min en Excel (BUSCARV +
/// formato a mano) del AS-IS.</para>
/// </summary>
public sealed record AntiguedadSaldosQuery(
    DateOnly? FechaCorte = null,
    Guid? ClienteId = null,
    string? Moneda = null) : IRequest<ReporteJsonResponse>;

public sealed class AntiguedadSaldosHandler : IRequestHandler<AntiguedadSaldosQuery, ReporteJsonResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    private readonly BucketsAntiguedadCxc _buckets;
    private readonly IClienteReadPort _clientes;
    private readonly IClock _clock;

    public AntiguedadSaldosHandler(
        CuentasPorCobrarDbContext db,
        BucketsAntiguedadCxc buckets,
        IClienteReadPort clientes,
        IClock clock)
    {
        _db = db; _buckets = buckets; _clientes = clientes; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        AntiguedadSaldosQuery query, CancellationToken cancellationToken)
    {
        var fechaCorte = query.FechaCorte ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var q = _db.FacturasCartera.AsNoTracking()
            .Where(f => f.Estado == EstadoFacturaCartera.Abierta
                     || f.Estado == EstadoFacturaCartera.Parcial);
        if (query.ClienteId is Guid c) q = q.Where(f => f.ClienteId == c);
        if (!string.IsNullOrWhiteSpace(query.Moneda)) q = q.Where(f => f.Moneda == query.Moneda);

        var facturas = await q
            .Select(f => new
            {
                f.ClienteId,
                f.ReceptorRfc,
                f.ReceptorNombre,
                f.Moneda,
                f.FechaVencimiento,
                Saldo = f.Total - f.MontoPagado - f.MontoNc,
            })
            .ToListAsync(cancellationToken);

        // Clasificación A/B/C/E desde la línea activa del (cliente, moneda).
        var clasificaciones = await _db.LineasCredito.AsNoTracking()
            .Where(l => l.Estado == EstadoLineaCredito.Activa && l.Clasificacion != null)
            .Select(l => new { l.ClienteId, l.Moneda, l.Clasificacion })
            .ToListAsync(cancellationToken);
        var clasificacionPor = clasificaciones
            .ToDictionary(x => (x.ClienteId, x.Moneda), x => x.Clasificacion);

        var keys = _buckets.Keys;
        var agrupado = facturas
            .GroupBy(f => (f.ClienteId, f.ReceptorRfc, f.ReceptorNombre, f.Moneda))
            .Select(g =>
            {
                var porBucket = keys.ToDictionary(k => k, _ => 0m);
                var porVencer = 0m;
                foreach (var f in g)
                {
                    var bucket = _buckets.Calcular(
                        DateOnly.FromDateTime(f.FechaVencimiento.UtcDateTime), fechaCorte);
                    if (bucket == BucketsAntiguedadCxc.PorVencer) porVencer += f.Saldo;
                    else porBucket[bucket] += f.Saldo;
                }

                return new
                {
                    g.Key.ClienteId,
                    g.Key.ReceptorRfc,
                    g.Key.ReceptorNombre,
                    g.Key.Moneda,
                    Clasificacion = g.Key.ClienteId is Guid cid
                        ? clasificacionPor.GetValueOrDefault((cid, g.Key.Moneda))
                        : null,
                    PorVencer = porVencer,
                    PorBucket = porBucket,
                    Vencido = porBucket.Values.Sum(),
                    NumeroFacturas = g.Count(),
                };
            })
            .OrderByDescending(x => x.Vencido)
            .ToList();

        var filas = agrupado
            .Select(g =>
            {
                var fila = new Dictionary<string, object?>
                {
                    ["rfc"] = g.ReceptorRfc,
                    ["cliente"] = g.ReceptorNombre,
                    ["clasificacion"] = g.Clasificacion,
                    ["moneda"] = g.Moneda,
                    [BucketsAntiguedadCxc.PorVencer] = g.PorVencer,
                };
                foreach (var k in keys) fila[k] = g.PorBucket[k];
                fila["vencido"] = g.Vencido;
                fila["total"] = g.PorVencer + g.Vencido;
                fila["numero_facturas"] = g.NumeroFacturas;
                return (IReadOnlyDictionary<string, object?>)fila;
            })
            .ToList();

        var totales = new Dictionary<string, object?>
        {
            [BucketsAntiguedadCxc.PorVencer] = agrupado.Sum(x => x.PorVencer),
        };
        foreach (var k in keys) totales[k] = agrupado.Sum(x => x.PorBucket[k]);
        totales["vencido"] = agrupado.Sum(x => x.Vencido);
        totales["total"] = agrupado.Sum(x => x.PorVencer + x.Vencido);
        totales["numero_facturas"] = agrupado.Sum(x => x.NumeroFacturas);

        var columnas = new List<ColumnaReporte>
        {
            new("rfc",           "RFC",           TipoColumnaReporte.Texto,  AlineacionColumna.Izquierda),
            new("cliente",       "Cliente",       TipoColumnaReporte.Texto,  AlineacionColumna.Izquierda),
            new("clasificacion", "Clasif.",       TipoColumnaReporte.Texto,  AlineacionColumna.Centro),
            new("moneda",        "Moneda",        TipoColumnaReporte.Texto,  AlineacionColumna.Centro),
            new(BucketsAntiguedadCxc.PorVencer, "Por vencer", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
        };
        columnas.AddRange(keys.Zip(_buckets.Labels.Take(keys.Count))
            .Select(p => new ColumnaReporte(p.First, p.Second, TipoColumnaReporte.Moneda, AlineacionColumna.Derecha)));
        columnas.Add(new("vencido", "Vencido", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha));
        columnas.Add(new("total", "Total", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha));
        columnas.Add(new("numero_facturas", "# Facturas", TipoColumnaReporte.Entero, AlineacionColumna.Derecha));

        var filtros = new List<FiltroAplicado> { new("Fecha de corte", fechaCorte.ToString("yyyy-MM-dd")) };
        if (query.ClienteId is Guid cf)
        {
            // Razón social en vez del UUID interno (feedback 2026-07-14).
            var cliente = await _clientes.ObtenerAsync(cf, cancellationToken);
            filtros.Add(new("Cliente", cliente?.RazonSocial ?? cf.ToString()));
        }
        if (!string.IsNullOrWhiteSpace(query.Moneda)) filtros.Add(new("Moneda", query.Moneda));

        return new ReporteJsonResponse(
            Titulo: "Antigüedad de saldos de cartera",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: filtros,
            Columnas: columnas,
            Filas: filas,
            Totales: totales);
    }
}

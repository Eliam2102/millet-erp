using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Reportes.AntiguedadSaldos;

/// <summary>
/// Reporte de antigüedad de saldos de cuentas por pagar (F8-PR1,
/// ADR-0036). Agrupa facturas vivas (no canceladas, no totalmente
/// pagadas) por proveedor + buckets 0-30 / 31-60 / 61-90 / +90 días
/// vs fecha de corte.
///
/// <para>
/// Filtros: <see cref="FechaCorte"/> (default = hoy UTC), opcional
/// <see cref="ProveedorId"/> y <see cref="SucursalId"/>. Sólo
/// facturas con <c>SaldoPendiente &gt; 0</c>.
/// </para>
/// </summary>
public sealed record AntiguedadSaldosProveedoresQuery(
    DateOnly? FechaCorte = null,
    Guid? ProveedorId = null,
    Guid? SucursalId = null) : IRequest<ReporteJsonResponse>;

public sealed class AntiguedadSaldosProveedoresHandler
    : IRequestHandler<AntiguedadSaldosProveedoresQuery, ReporteJsonResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly SharedKernel.Application.IClock _clock;

    public AntiguedadSaldosProveedoresHandler(
        CuentasPorPagarDbContext db, SharedKernel.Application.IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        AntiguedadSaldosProveedoresQuery query, CancellationToken cancellationToken)
    {
        var fechaCorte = query.FechaCorte ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var q = _db.FacturasProveedor.AsNoTracking()
            .Where(f =>
                f.Estado != EstadoPasivo.Cancelada
                && (f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.ImportePagado) > 0);

        if (query.ProveedorId is Guid p) q = q.Where(f => f.ProveedorId == p);
        if (query.SucursalId is Guid s) q = q.Where(f => f.SucursalId == s);

        var facturas = await q
            .Select(f => new
            {
                f.ProveedorId,
                f.FechaVencimiento,
                Saldo = f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.ImportePagado,
                f.Moneda,
            })
            .ToListAsync(cancellationToken);

        // Agrupar por proveedor y proyectar buckets.
        var agrupado = facturas
            .GroupBy(f => f.ProveedorId)
            .Select(g => new
            {
                ProveedorId = g.Key,
                B0_30 = g.Where(x => BucketsAntiguedad.CalcularBucket(x.FechaVencimiento, fechaCorte) == BucketsAntiguedad.Bucket0a30).Sum(x => x.Saldo),
                B31_60 = g.Where(x => BucketsAntiguedad.CalcularBucket(x.FechaVencimiento, fechaCorte) == BucketsAntiguedad.Bucket31a60).Sum(x => x.Saldo),
                B61_90 = g.Where(x => BucketsAntiguedad.CalcularBucket(x.FechaVencimiento, fechaCorte) == BucketsAntiguedad.Bucket61a90).Sum(x => x.Saldo),
                BMas90 = g.Where(x => BucketsAntiguedad.CalcularBucket(x.FechaVencimiento, fechaCorte) == BucketsAntiguedad.BucketMas90).Sum(x => x.Saldo),
                NumeroFacturas = g.Count(),
                Moneda = g.First().Moneda,
            })
            .OrderByDescending(x => x.B0_30 + x.B31_60 + x.B61_90 + x.BMas90)
            .ToList();

        var filas = agrupado
            .Select<dynamic, IReadOnlyDictionary<string, object?>>(g => new Dictionary<string, object?>
            {
                ["proveedor_id"]    = g.ProveedorId,
                ["moneda"]          = g.Moneda,
                ["b0_30"]           = g.B0_30,
                ["b31_60"]          = g.B31_60,
                ["b61_90"]          = g.B61_90,
                ["bMas90"]          = g.BMas90,
                ["total"]           = g.B0_30 + g.B31_60 + g.B61_90 + g.BMas90,
                ["numero_facturas"] = g.NumeroFacturas,
            })
            .ToList();

        var totales = new Dictionary<string, object?>
        {
            ["b0_30"]           = agrupado.Sum(x => x.B0_30),
            ["b31_60"]          = agrupado.Sum(x => x.B31_60),
            ["b61_90"]          = agrupado.Sum(x => x.B61_90),
            ["bMas90"]          = agrupado.Sum(x => x.BMas90),
            ["total"]           = agrupado.Sum(x => x.B0_30 + x.B31_60 + x.B61_90 + x.BMas90),
            ["numero_facturas"] = agrupado.Sum(x => x.NumeroFacturas),
        };

        return new ReporteJsonResponse(
            Titulo: "Antigüedad de saldos por proveedor",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: ConstruirFiltros(fechaCorte, query.ProveedorId, query.SucursalId),
            Columnas: new[]
            {
                new ColumnaReporte("proveedor_id",    "Proveedor",          TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("moneda",          "Moneda",             TipoColumnaReporte.Texto,   AlineacionColumna.Centro),
                new ColumnaReporte("b0_30",           "0-30 días",          TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("b31_60",          "31-60 días",         TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("b61_90",          "61-90 días",         TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("bMas90",          "+90 días",           TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("total",           "Total",              TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("numero_facturas", "# Facturas",         TipoColumnaReporte.Entero,  AlineacionColumna.Derecha),
            },
            Filas: filas,
            Totales: totales);
    }

    private static List<FiltroAplicado> ConstruirFiltros(
        DateOnly fechaCorte, Guid? proveedorId, Guid? sucursalId)
    {
        var lista = new List<FiltroAplicado>
        {
            new("Fecha de corte", fechaCorte.ToString("yyyy-MM-dd")),
        };
        if (proveedorId is Guid p) lista.Add(new("Proveedor", p.ToString()));
        if (sucursalId is Guid s) lista.Add(new("Sucursal", s.ToString()));
        return lista;
    }
}

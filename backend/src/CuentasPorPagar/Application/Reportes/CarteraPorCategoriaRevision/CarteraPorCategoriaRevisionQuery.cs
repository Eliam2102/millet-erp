using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Reportes.CarteraPorCategoriaRevision;

/// <summary>
/// Reporte cruz de cartera de proveedores: <b>proveedor × estado de
/// revisión × bucket de antigüedad</b> (F8-PR1, §13.1 punto 1 del
/// 00-levantamiento).
///
/// <para>
/// Spec del breakdown define "subcategoría × revisión × antigüedad",
/// pero la subcategoría del proveedor vive en <c>DatosMaestros</c> —
/// CxP no la accede directamente. PLATFORM-TODO(<c>ProveedorReadPortConSubcategoria</c>):
/// cuando <c>IProveedorReadPort</c> exponga la subcategoría, mapear
/// proveedor → subcategoría aquí. Por ahora la dimensión "subcategoría"
/// se materializa en el frontend agrupando filas por proveedor.
/// </para>
///
/// <para>
/// Una fila por (proveedor, en_revision); columnas: 4 buckets de
/// antigüedad + total + #facturas. Total filas incluye dos rows por
/// proveedor que tenga facturas tanto en revisión como autorizadas.
/// </para>
/// </summary>
public sealed record CarteraPorCategoriaRevisionQuery(
    DateOnly? FechaCorte = null,
    Guid? ProveedorId = null,
    Guid? SucursalId = null,
    bool? SoloEnRevision = null) : IRequest<ReporteJsonResponse>;

public sealed class CarteraPorCategoriaRevisionHandler
    : IRequestHandler<CarteraPorCategoriaRevisionQuery, ReporteJsonResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly SharedKernel.Application.IClock _clock;

    public CarteraPorCategoriaRevisionHandler(
        CuentasPorPagarDbContext db, SharedKernel.Application.IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        CarteraPorCategoriaRevisionQuery query, CancellationToken cancellationToken)
    {
        var fechaCorte = query.FechaCorte ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var q = _db.FacturasProveedor.AsNoTracking()
            .Where(f =>
                f.Estado != EstadoPasivo.Cancelada
                && (f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.ImportePagado) > 0);

        if (query.ProveedorId is Guid p) q = q.Where(f => f.ProveedorId == p);
        if (query.SucursalId is Guid s) q = q.Where(f => f.SucursalId == s);
        if (query.SoloEnRevision is bool en) q = q.Where(f => f.EnRevision == en);

        var facturas = await q
            .Select(f => new
            {
                f.ProveedorId,
                f.EnRevision,
                f.FechaVencimiento,
                Saldo = f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.ImportePagado,
                f.Moneda,
            })
            .ToListAsync(cancellationToken);

        // Agrupar por (proveedor, en_revision).
        var agrupado = facturas
            .GroupBy(f => new { f.ProveedorId, f.EnRevision })
            .Select(g => new
            {
                g.Key.ProveedorId,
                g.Key.EnRevision,
                B0_30 = g.Where(x => BucketsAntiguedad.CalcularBucket(x.FechaVencimiento, fechaCorte) == BucketsAntiguedad.Bucket0a30).Sum(x => x.Saldo),
                B31_60 = g.Where(x => BucketsAntiguedad.CalcularBucket(x.FechaVencimiento, fechaCorte) == BucketsAntiguedad.Bucket31a60).Sum(x => x.Saldo),
                B61_90 = g.Where(x => BucketsAntiguedad.CalcularBucket(x.FechaVencimiento, fechaCorte) == BucketsAntiguedad.Bucket61a90).Sum(x => x.Saldo),
                BMas90 = g.Where(x => BucketsAntiguedad.CalcularBucket(x.FechaVencimiento, fechaCorte) == BucketsAntiguedad.BucketMas90).Sum(x => x.Saldo),
                NumeroFacturas = g.Count(),
                Moneda = g.First().Moneda,
            })
            .OrderBy(x => x.ProveedorId).ThenByDescending(x => x.EnRevision)
            .ToList();

        var filas = agrupado
            .Select<dynamic, IReadOnlyDictionary<string, object?>>(g => new Dictionary<string, object?>
            {
                ["proveedor_id"]    = g.ProveedorId,
                ["en_revision"]     = g.EnRevision,
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
            ["total_general"]               = agrupado.Sum(x => x.B0_30 + x.B31_60 + x.B61_90 + x.BMas90),
            ["total_en_revision"]           = agrupado.Where(x => x.EnRevision).Sum(x => x.B0_30 + x.B31_60 + x.B61_90 + x.BMas90),
            ["total_no_revision"]           = agrupado.Where(x => !x.EnRevision).Sum(x => x.B0_30 + x.B31_60 + x.B61_90 + x.BMas90),
            ["numero_facturas_en_revision"] = agrupado.Where(x => x.EnRevision).Sum(x => x.NumeroFacturas),
            ["numero_facturas_total"]       = agrupado.Sum(x => x.NumeroFacturas),
        };

        return new ReporteJsonResponse(
            Titulo: "Cartera de proveedores — cruz revisión × antigüedad",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: ConstruirFiltros(fechaCorte, query.ProveedorId, query.SucursalId, query.SoloEnRevision),
            Columnas: new[]
            {
                new ColumnaReporte("proveedor_id",    "Proveedor",      TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("en_revision",     "En revisión",    TipoColumnaReporte.Booleano, AlineacionColumna.Centro),
                new ColumnaReporte("moneda",          "Moneda",         TipoColumnaReporte.Texto,   AlineacionColumna.Centro),
                new ColumnaReporte("b0_30",           "0-30 días",      TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("b31_60",          "31-60 días",     TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("b61_90",          "61-90 días",     TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("bMas90",          "+90 días",       TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("total",           "Total",          TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("numero_facturas", "# Facturas",     TipoColumnaReporte.Entero,  AlineacionColumna.Derecha),
            },
            Filas: filas,
            Totales: totales);
    }

    private static List<FiltroAplicado> ConstruirFiltros(
        DateOnly fechaCorte, Guid? proveedorId, Guid? sucursalId, bool? soloEnRevision)
    {
        var lista = new List<FiltroAplicado>
        {
            new("Fecha de corte", fechaCorte.ToString("yyyy-MM-dd")),
        };
        if (proveedorId is Guid p) lista.Add(new("Proveedor", p.ToString()));
        if (sucursalId is Guid s) lista.Add(new("Sucursal", s.ToString()));
        if (soloEnRevision is bool e) lista.Add(new("Solo en revisión", e ? "Sí" : "No"));
        return lista;
    }
}

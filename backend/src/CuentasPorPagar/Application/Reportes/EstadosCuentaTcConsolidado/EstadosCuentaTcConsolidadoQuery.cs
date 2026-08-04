using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Reportes.EstadosCuentaTcConsolidado;

/// <summary>
/// Reporte consolidado de estados de cuenta TC (F8-PR2). Cada fila es
/// un <c>EstadoCuentaTc</c> con totales por estado match
/// (auto/sugerencia/sin-match), diferencia banco-conciliado y la
/// factura agregada generada (si está Cerrado).
/// </summary>
public sealed record EstadosCuentaTcConsolidadoQuery(
    Guid? TarjetaId = null,
    EstadoCuentaTcStatus? Estado = null,
    DateOnly? PeriodoDesde = null,
    DateOnly? PeriodoHasta = null) : IRequest<ReporteJsonResponse>;

public sealed class EstadosCuentaTcConsolidadoHandler
    : IRequestHandler<EstadosCuentaTcConsolidadoQuery, ReporteJsonResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly SharedKernel.Application.IClock _clock;

    public EstadosCuentaTcConsolidadoHandler(
        CuentasPorPagarDbContext db, SharedKernel.Application.IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        EstadosCuentaTcConsolidadoQuery query, CancellationToken cancellationToken)
    {
        var q = _db.EstadosCuentaTc.AsNoTracking()
            .Include(e => e.Lineas);

        IQueryable<EstadoCuentaTc> filtered = q;
        if (query.TarjetaId is Guid t) filtered = filtered.Where(e => e.TarjetaId == t);
        if (query.Estado is EstadoCuentaTcStatus s) filtered = filtered.Where(e => e.Estado == s);
        if (query.PeriodoDesde is DateOnly pd) filtered = filtered.Where(e => e.PeriodoHasta >= pd);
        if (query.PeriodoHasta is DateOnly ph) filtered = filtered.Where(e => e.PeriodoDesde <= ph);

        var estados = await filtered
            .OrderByDescending(e => e.FechaCorte)
            .ToListAsync(cancellationToken);

        var filas = estados
            .Select<EstadoCuentaTc, IReadOnlyDictionary<string, object?>>(e => new Dictionary<string, object?>
            {
                ["estado_cuenta_id"]    = e.Id,
                ["tarjeta_id"]          = e.TarjetaId,
                ["periodo_desde"]       = e.PeriodoDesde,
                ["periodo_hasta"]       = e.PeriodoHasta,
                ["fecha_corte"]         = e.FechaCorte,
                ["fecha_limite_pago"]   = e.FechaLimitePago,
                ["estado"]              = e.Estado.ToString(),
                ["total_banco_mxn"]     = e.TotalBancoMxn,
                ["total_conciliado_mxn"] = e.TotalConciliadoMxn,
                ["diferencia_mxn"]      = e.DiferenciaMxn,
                ["diferencia_cambiaria_mxn"] = e.DiferenciaCambiariaMxn,
                ["lineas_total"]        = e.Lineas.Count,
                ["lineas_matched"]      = e.Lineas.Count(l => l.EstadoMatch == EstadoMatchLineaBanco.Matched),
                ["lineas_pendientes"]   = e.Lineas.Count(l => l.EstadoMatch == EstadoMatchLineaBanco.Pendiente),
                ["lineas_sin_match"]    = e.Lineas.Count(l => l.EstadoMatch == EstadoMatchLineaBanco.NoConciliado),
                ["factura_proveedor_id"] = e.FacturaProveedorId,
                ["perfil_parser"]       = e.PerfilParserUsado,
            })
            .ToList();

        var totales = new Dictionary<string, object?>
        {
            ["numero_estados_cuenta"]     = estados.Count,
            ["total_banco_acumulado_mxn"] = estados.Sum(e => e.TotalBancoMxn ?? 0m),
            ["total_conciliado_acumulado_mxn"] = estados.Sum(e => e.TotalConciliadoMxn ?? 0m),
            ["lineas_pendientes_total"]   = estados.Sum(e => e.Lineas.Count(l => l.EstadoMatch == EstadoMatchLineaBanco.Pendiente)),
            ["lineas_sin_match_total"]    = estados.Sum(e => e.Lineas.Count(l => l.EstadoMatch == EstadoMatchLineaBanco.NoConciliado)),
        };

        return new ReporteJsonResponse(
            Titulo: "Estados de cuenta TC consolidado",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: ConstruirFiltros(query),
            Columnas: new[]
            {
                new ColumnaReporte("tarjeta_id",              "Tarjeta",          TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("periodo_desde",           "Periodo desde",    TipoColumnaReporte.Fecha,   AlineacionColumna.Centro),
                new ColumnaReporte("periodo_hasta",           "Periodo hasta",    TipoColumnaReporte.Fecha,   AlineacionColumna.Centro),
                new ColumnaReporte("fecha_corte",             "Corte",            TipoColumnaReporte.Fecha,   AlineacionColumna.Centro),
                new ColumnaReporte("fecha_limite_pago",       "Límite pago",      TipoColumnaReporte.Fecha,   AlineacionColumna.Centro),
                new ColumnaReporte("estado",                  "Estado",           TipoColumnaReporte.Enum,    AlineacionColumna.Centro),
                new ColumnaReporte("total_banco_mxn",         "Total banco",      TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("total_conciliado_mxn",    "Conciliado",       TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("diferencia_mxn",          "Diferencia",       TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("diferencia_cambiaria_mxn", "Dif. cambiaria",  TipoColumnaReporte.Moneda,  AlineacionColumna.Derecha),
                new ColumnaReporte("lineas_total",            "Líneas",           TipoColumnaReporte.Entero,  AlineacionColumna.Derecha),
                new ColumnaReporte("lineas_matched",          "Matched",          TipoColumnaReporte.Entero,  AlineacionColumna.Derecha),
                new ColumnaReporte("lineas_pendientes",       "Pendientes",       TipoColumnaReporte.Entero,  AlineacionColumna.Derecha),
                new ColumnaReporte("lineas_sin_match",        "Sin match",        TipoColumnaReporte.Entero,  AlineacionColumna.Derecha),
                new ColumnaReporte("factura_proveedor_id",    "Factura banco",    TipoColumnaReporte.Texto,   AlineacionColumna.Izquierda),
                new ColumnaReporte("perfil_parser",           "Perfil",           TipoColumnaReporte.Texto,   AlineacionColumna.Centro),
            },
            Filas: filas,
            Totales: totales);
    }

    private static List<FiltroAplicado> ConstruirFiltros(EstadosCuentaTcConsolidadoQuery q)
    {
        var lista = new List<FiltroAplicado>();
        if (q.TarjetaId is Guid t) lista.Add(new("Tarjeta", t.ToString()));
        if (q.Estado is EstadoCuentaTcStatus s) lista.Add(new("Estado", s.ToString()));
        if (q.PeriodoDesde is DateOnly d) lista.Add(new("Periodo desde", d.ToString("yyyy-MM-dd")));
        if (q.PeriodoHasta is DateOnly h) lista.Add(new("Periodo hasta", h.ToString("yyyy-MM-dd")));
        return lista;
    }
}

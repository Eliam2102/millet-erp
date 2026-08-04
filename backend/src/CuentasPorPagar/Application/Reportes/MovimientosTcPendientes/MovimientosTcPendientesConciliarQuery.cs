using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Reportes.Comun;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Reportes.MovimientosTcPendientes;

/// <summary>
/// Reporte de movimientos de TC empresarial que están en estado
/// <see cref="EstadoMovimientoTc.Registrado"/> y aún no han sido
/// conciliados con un estado de cuenta del banco (F8-PR2, §7 anexo TC).
///
/// <para>
/// Utility operativa: la tarjeta acaba de tener su corte y el operador
/// quiere ver qué cargos quedaron sin conciliar al subir el archivo
/// del banco — sugiere capturas retroactivas faltantes o discrepancias
/// con el banco.
/// </para>
/// </summary>
public sealed record MovimientosTcPendientesConciliarQuery(
    Guid? TarjetaId = null,
    Guid? UsuarioQueUsoId = null,
    DateOnly? FechaDesde = null,
    DateOnly? FechaHasta = null) : IRequest<ReporteJsonResponse>;

public sealed class MovimientosTcPendientesConciliarHandler
    : IRequestHandler<MovimientosTcPendientesConciliarQuery, ReporteJsonResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly SharedKernel.Application.IClock _clock;

    public MovimientosTcPendientesConciliarHandler(
        CuentasPorPagarDbContext db, SharedKernel.Application.IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        MovimientosTcPendientesConciliarQuery query, CancellationToken cancellationToken)
    {
        var q = _db.MovimientosTarjetaCredito.AsNoTracking()
            .Where(m => m.Estado == EstadoMovimientoTc.Registrado
                        && m.EstadoCuentaTcId == null);

        if (query.TarjetaId is Guid t) q = q.Where(m => m.TarjetaId == t);
        if (query.UsuarioQueUsoId is Guid u) q = q.Where(m => m.UsuarioQueUsoId == u);
        if (query.FechaDesde is DateOnly d) q = q.Where(m => m.FechaMovimiento >= d);
        if (query.FechaHasta is DateOnly h) q = q.Where(m => m.FechaMovimiento <= h);

        var movimientos = await q
            .OrderByDescending(m => m.FechaMovimiento)
            .ThenBy(m => m.TarjetaId)
            .Select(m => new
            {
                m.Id,
                m.TarjetaId,
                m.UsuarioQueUsoId,
                m.FechaMovimiento,
                m.Tipo,
                m.MerchantNormalizado,
                m.MontoOriginal,
                m.MonedaOriginal,
                m.MontoMxn,
                m.ConceptoContable,
                m.CapturaRetroactiva,
                m.EnDisputa,
            })
            .ToListAsync(cancellationToken);

        var filas = movimientos
            .Select<dynamic, IReadOnlyDictionary<string, object?>>(m => new Dictionary<string, object?>
            {
                ["movimiento_id"]      = m.Id,
                ["tarjeta_id"]         = m.TarjetaId,
                ["usuario_que_uso_id"] = m.UsuarioQueUsoId,
                ["fecha_movimiento"]   = m.FechaMovimiento,
                ["tipo"]               = m.Tipo.ToString(),
                ["merchant"]           = m.MerchantNormalizado,
                ["monto_original"]     = m.MontoOriginal,
                ["moneda_original"]    = m.MonedaOriginal,
                ["monto_mxn"]          = m.MontoMxn,
                ["concepto"]           = m.ConceptoContable,
                ["captura_retroactiva"] = m.CapturaRetroactiva,
                ["en_disputa"]         = m.EnDisputa,
            })
            .ToList();

        var totales = new Dictionary<string, object?>
        {
            ["total_movimientos"]    = movimientos.Count,
            ["total_monto_mxn"]      = movimientos.Sum(m => m.MontoMxn),
            ["en_disputa"]           = movimientos.Count(m => m.EnDisputa),
            ["capturas_retroactivas"] = movimientos.Count(m => m.CapturaRetroactiva),
        };

        return new ReporteJsonResponse(
            Titulo: "Movimientos de TC pendientes de conciliar",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: ConstruirFiltros(query),
            Columnas: new[]
            {
                new ColumnaReporte("tarjeta_id",          "Tarjeta",           TipoColumnaReporte.Texto,    AlineacionColumna.Izquierda),
                new ColumnaReporte("usuario_que_uso_id",  "Usuario",           TipoColumnaReporte.Texto,    AlineacionColumna.Izquierda),
                new ColumnaReporte("fecha_movimiento",    "Fecha",             TipoColumnaReporte.Fecha,    AlineacionColumna.Centro),
                new ColumnaReporte("tipo",                "Tipo",              TipoColumnaReporte.Enum,     AlineacionColumna.Centro),
                new ColumnaReporte("merchant",            "Comercio",          TipoColumnaReporte.Texto,    AlineacionColumna.Izquierda),
                new ColumnaReporte("monto_original",      "Monto original",    TipoColumnaReporte.Moneda,   AlineacionColumna.Derecha),
                new ColumnaReporte("moneda_original",     "Moneda",            TipoColumnaReporte.Texto,    AlineacionColumna.Centro),
                new ColumnaReporte("monto_mxn",           "Monto MXN",         TipoColumnaReporte.Moneda,   AlineacionColumna.Derecha),
                new ColumnaReporte("concepto",            "Concepto contable", TipoColumnaReporte.Texto,    AlineacionColumna.Izquierda),
                new ColumnaReporte("captura_retroactiva", "Capt. retro.",      TipoColumnaReporte.Booleano, AlineacionColumna.Centro),
                new ColumnaReporte("en_disputa",          "En disputa",        TipoColumnaReporte.Booleano, AlineacionColumna.Centro),
            },
            Filas: filas,
            Totales: totales);
    }

    private static List<FiltroAplicado> ConstruirFiltros(MovimientosTcPendientesConciliarQuery q)
    {
        var lista = new List<FiltroAplicado>();
        if (q.TarjetaId is Guid t) lista.Add(new("Tarjeta", t.ToString()));
        if (q.UsuarioQueUsoId is Guid u) lista.Add(new("Usuario", u.ToString()));
        if (q.FechaDesde is DateOnly d) lista.Add(new("Desde", d.ToString("yyyy-MM-dd")));
        if (q.FechaHasta is DateOnly h) lista.Add(new("Hasta", h.ToString("yyyy-MM-dd")));
        return lista;
    }
}

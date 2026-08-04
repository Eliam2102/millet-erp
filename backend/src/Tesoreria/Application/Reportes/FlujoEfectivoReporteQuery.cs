using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.Reportes.Comun;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Reportes;

// ============================================================================
// TES-PR10: reporte de flujo de efectivo (§7 del levantamiento, TES-6).
// Clasificación por ConceptoMovimiento (Operación/Inversión/Financiamiento);
// reemplaza la plantilla Excel sobre Libro Mayor SAP (mensual, 3-4 h
// manuales). Motor nativo ADR-0036: JSON estructurado, sin lógica de
// reporte en el dominio. Los contramovimientos netean solos (pares
// ingreso/egreso ligados). ⚠️ Catálogo de conceptos provisional (Javier
// valida, §5.2).
// ============================================================================

public sealed record FlujoEfectivoReporteQuery(
    DateOnly Desde,
    DateOnly Hasta,
    Guid? CuentaBancariaId = null,
    string? Moneda = null) : IRequest<ReporteJsonResponse>;

public sealed class FlujoEfectivoReporteValidator : AbstractValidator<FlujoEfectivoReporteQuery>
{
    public FlujoEfectivoReporteValidator()
    {
        RuleFor(q => q.Desde).NotEmpty();
        RuleFor(q => q.Hasta).NotEmpty().GreaterThanOrEqualTo(q => q.Desde);
    }
}

public sealed class FlujoEfectivoReporteHandler
    : IRequestHandler<FlujoEfectivoReporteQuery, ReporteJsonResponse>
{
    private static readonly string[] NombresClasificacion =
        ["", "Operación", "Inversión", "Financiamiento"];

    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;

    public FlujoEfectivoReporteHandler(TesoreriaDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        FlujoEfectivoReporteQuery query, CancellationToken cancellationToken)
    {
        var q = _db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.FechaValor >= query.Desde && m.FechaValor <= query.Hasta);
        if (query.CuentaBancariaId is Guid cuenta) q = q.Where(m => m.CuentaBancariaId == cuenta);
        if (!string.IsNullOrWhiteSpace(query.Moneda)) q = q.Where(m => m.Moneda == query.Moneda);

        // Agregación en SQL por (moneda, concepto, sentido).
        var agregados = await q
            .GroupBy(m => new { m.Moneda, m.ConceptoId, m.Sentido })
            .Select(g => new
            {
                g.Key.Moneda,
                g.Key.ConceptoId,
                g.Key.Sentido,
                Total = g.Sum(m => m.Monto),
            })
            .ToListAsync(cancellationToken);

        var conceptos = await _db.ConceptosMovimiento.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var filas = agregados
            .GroupBy(a => new { a.Moneda, a.ConceptoId })
            .Select(g =>
            {
                var concepto = g.Key.ConceptoId is Guid cid && conceptos.TryGetValue(cid, out var c) ? c : null;
                var ingresos = g.Where(x => x.Sentido == SentidoMovimiento.Ingreso).Sum(x => x.Total);
                var egresos = g.Where(x => x.Sentido == SentidoMovimiento.Egreso).Sum(x => x.Total);
                return new
                {
                    Clasificacion = concepto is null
                        ? (short)ClasificacionFlujo.Operacion
                        : (short)concepto.ClasificacionFlujo,
                    ConceptoNombre = concepto?.Nombre ?? "(Sin concepto)",
                    g.Key.Moneda,
                    Ingresos = ingresos,
                    Egresos = egresos,
                    Neto = ingresos - egresos,
                };
            })
            .OrderBy(f => f.Clasificacion).ThenBy(f => f.ConceptoNombre).ThenBy(f => f.Moneda)
            .Select(f => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["clasificacion"] = NombresClasificacion[f.Clasificacion],
                ["concepto"] = f.ConceptoNombre,
                ["moneda"] = f.Moneda,
                ["ingresos"] = f.Ingresos,
                ["egresos"] = f.Egresos,
                ["neto"] = f.Neto,
            })
            .ToList();

        var totalIngresos = agregados.Where(a => a.Sentido == SentidoMovimiento.Ingreso).Sum(a => a.Total);
        var totalEgresos = agregados.Where(a => a.Sentido == SentidoMovimiento.Egreso).Sum(a => a.Total);

        var filtros = new List<FiltroAplicado>
        {
            new("Del", query.Desde.ToString("yyyy-MM-dd")),
            new("Al", query.Hasta.ToString("yyyy-MM-dd")),
        };
        if (query.CuentaBancariaId is not null) filtros.Add(new("Cuenta", query.CuentaBancariaId.ToString()!));
        if (!string.IsNullOrWhiteSpace(query.Moneda)) filtros.Add(new("Moneda", query.Moneda));

        return new ReporteJsonResponse(
            Titulo: "Flujo de efectivo",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados: filtros,
            Columnas:
            [
                new("clasificacion", "Clasificación", TipoColumnaReporte.Texto,  AlineacionColumna.Izquierda),
                new("concepto",      "Concepto",      TipoColumnaReporte.Texto,  AlineacionColumna.Izquierda),
                new("moneda",        "Moneda",        TipoColumnaReporte.Texto,  AlineacionColumna.Centro),
                new("ingresos",      "Ingresos",      TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("egresos",       "Egresos",       TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
                new("neto",          "Neto",          TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
            ],
            Filas: filas,
            Totales: new Dictionary<string, object?>
            {
                ["ingresos"] = totalIngresos,
                ["egresos"] = totalEgresos,
                ["neto"] = totalIngresos - totalEgresos,
            });
    }
}

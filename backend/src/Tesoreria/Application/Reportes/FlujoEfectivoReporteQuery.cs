using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Reportes.Comun;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Reportes;

public sealed record FlujoEfectivoReporteQuery(DateOnly Desde, DateOnly Hasta,
    Guid? CuentaBancariaId = null, string? Moneda = null) : IRequest<ReporteJsonResponse>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "tesoreria.documentos.leer-todas-sucursales";
    public string TipoDocumento => "cuenta_bancaria";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}
public sealed class FlujoEfectivoReporteValidator : AbstractValidator<FlujoEfectivoReporteQuery>
{
    public FlujoEfectivoReporteValidator()
    {
        RuleFor(q => q.Desde).NotEmpty();
        RuleFor(q => q.Hasta).NotEmpty().GreaterThanOrEqualTo(q => q.Desde);
    }
}

public sealed class FlujoEfectivoReporteHandler(TesoreriaDbContext db, IClock clock)
    : IRequestHandler<FlujoEfectivoReporteQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(FlujoEfectivoReporteQuery query, CancellationToken cancellationToken)
    {
        var cuentas = await db.CuentasBancarias.AsNoTracking()
            .Where(c => query.DocumentosPermitidos == null || (query.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(c.Id))
            .Where(c => (query.CuentaBancariaId == null || c.Id == query.CuentaBancariaId) &&
                (query.Moneda == null || c.Moneda == query.Moneda))
            .OrderBy(c => c.Moneda).ThenBy(c => c.Banco).ToListAsync(cancellationToken);
        if (cuentas.Any(c => c.FechaCorteSaldoInicial >= query.Desde))
            throw new BusinessRuleException("FLUJO_ANTERIOR_AL_CORTE", "El inicio del reporte debe ser posterior al corte del saldo inicial de las cuentas seleccionadas.");
        var ids = cuentas.Select(c => c.Id).ToList();
        var movimientos = db.MovimientosBancarios.AsNoTracking().Where(m => ids.Contains(m.CuentaBancariaId));
        var anteriores = await movimientos.Where(m => m.FechaValor < query.Desde)
            .GroupBy(m => m.CuentaBancariaId)
            .Select(g => new { Id = g.Key, Neto = g.Sum(m => m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto) })
            .ToDictionaryAsync(g => g.Id, g => g.Neto, cancellationToken);
        var agregados = await movimientos.Where(m => m.FechaValor >= query.Desde && m.FechaValor <= query.Hasta)
            .GroupBy(m => new { m.CuentaBancariaId, m.ConceptoId, m.Sentido })
            .Select(g => new { g.Key.CuentaBancariaId, g.Key.ConceptoId, g.Key.Sentido, Total = g.Sum(m => m.Monto) }).ToListAsync(cancellationToken);
        var conceptos = await db.ConceptosMovimiento.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var filas = new List<IReadOnlyDictionary<string, object?>>();
        foreach (var cuenta in cuentas)
        {
            var inicial = (cuenta.SaldoInicial ?? 0) + anteriores.GetValueOrDefault(cuenta.Id);
            var cuentaNombre = $"{cuenta.Banco} {Clabe.Enmascarar(cuenta.NumeroCuenta)}";
            IReadOnlyDictionary<string, object?> Fila(string tipo, string clasificacion, string concepto,
                decimal? ingresos, decimal? egresos, decimal neto) => new Dictionary<string, object?>
                {
                    ["tipo"] = tipo, ["cuentaId"] = cuenta.Id, ["cuenta"] = cuentaNombre,
                    ["moneda"] = cuenta.Moneda, ["clasificacion"] = clasificacion, ["concepto"] = concepto,
                    ["ingresos"] = ingresos, ["egresos"] = egresos, ["neto"] = neto,
                };
            filas.Add(Fila("saldoInicial", "Saldo inicial", cuenta.SaldoInicial is null ? "Saldo inicial por capturar; solo movimientos registrados" : "Saldo al inicio del período", null, null, inicial));
            var periodo = agregados.Where(a => a.CuentaBancariaId == cuenta.Id).ToList();
            foreach (var grupo in periodo.GroupBy(a => a.ConceptoId).OrderBy(g => g.Key))
            {
                var concepto = grupo.Key is Guid id ? conceptos.GetValueOrDefault(id) : null;
                var clasificacion = concepto?.ClasificacionFlujo switch
                {
                    ClasificacionFlujo.Operacion => "Operación", ClasificacionFlujo.Inversion => "Inversión",
                    ClasificacionFlujo.Financiamiento => "Financiamiento", _ => "Sin clasificar",
                };
                var ingresos = grupo.Where(a => a.Sentido == SentidoMovimiento.Ingreso).Sum(a => a.Total);
                var egresos = grupo.Where(a => a.Sentido == SentidoMovimiento.Egreso).Sum(a => a.Total);
                filas.Add(Fila("movimiento", clasificacion, concepto?.Nombre ?? "(Sin concepto)", ingresos, egresos, ingresos - egresos));
            }
            var neto = periodo.Sum(a => a.Sentido == SentidoMovimiento.Ingreso ? a.Total : -a.Total);
            filas.Add(Fila("saldoFinal", "Saldo final", "Saldo al cierre del período", null, null, inicial + neto));
        }
        // Totales como filas para conservar separación por moneda también en PDF y Excel.
        foreach (var moneda in cuentas.Select(c => c.Moneda).Distinct())
        {
            var detalle = filas.Where(f => (string?)f["moneda"] == moneda).ToList();
            decimal Sumar(string tipo, string campo) => detalle.Where(f => (string?)f["tipo"] == tipo).Sum(f => (decimal?)f[campo] ?? 0);
            filas.Add(new Dictionary<string, object?>
            {
                ["tipo"] = "total", ["cuenta"] = $"Total {moneda}", ["moneda"] = moneda,
                ["clasificacion"] = "Total por moneda", ["concepto"] = "Flujo neto del período",
                ["ingresos"] = Sumar("movimiento", "ingresos"), ["egresos"] = Sumar("movimiento", "egresos"),
                ["neto"] = Sumar("movimiento", "neto"),
                ["saldoInicial"] = Sumar("saldoInicial", "neto"), ["saldoFinal"] = Sumar("saldoFinal", "neto"),
            });
        }
        return new ReporteJsonResponse("Flujo de efectivo", clock.UtcNow,
            [new("Del", query.Desde.ToString("yyyy-MM-dd")), new("Al", query.Hasta.ToString("yyyy-MM-dd"))],
            [new("cuenta", "Cuenta", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
             new("clasificacion", "Clasificación", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
             new("concepto", "Concepto", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
             new("moneda", "Moneda", TipoColumnaReporte.Texto, AlineacionColumna.Centro),
             new("ingresos", "Ingresos", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
             new("egresos", "Egresos", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
             new("neto", "Saldo / neto", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha)], filas, null);
    }
}

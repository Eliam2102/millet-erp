using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
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

public sealed class FlujoEfectivoReporteHandler(TesoreriaDbContext db, IClock clock, Millet.Tesoreria.Domain.Ports.ICorteNombresReadPort? nombres = null)
    : IRequestHandler<FlujoEfectivoReporteQuery, ReporteJsonResponse>
{
    public async Task<ReporteJsonResponse> Handle(FlujoEfectivoReporteQuery query, CancellationToken cancellationToken)
    {
        var cuentas = await FlujoLectura.Cuentas(db, query.DocumentosPermitidos, query.Moneda, query.CuentaBancariaId)
            .OrderBy(c => c.Moneda).ThenBy(c => c.Banco).ToListAsync(cancellationToken);
        // Una cuenta con corte dentro del rango arranca en su saldo al corte: no hay movimientos en o antes del corte
        // (MOV_ANTERIOR_AL_CORTE), así que el período de esa cuenta empieza al día siguiente (F-TES-08).
        var ids = cuentas.Select(c => c.Id).ToList();
        var movimientos = FlujoLectura.Movimientos(db, ids);
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
            var conceptoInicial = cuenta.SaldoInicial is null ? "Saldo inicial por capturar; solo movimientos registrados"
                : cuenta.FechaCorteSaldoInicial >= query.Desde ? $"Saldo al corte del {cuenta.FechaCorteSaldoInicial.Value.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)}"
                : "Saldo al inicio del período";
            filas.Add(Fila("saldoInicial", "Saldo inicial", conceptoInicial, null, null, inicial));
            var periodo = agregados.Where(a => a.CuentaBancariaId == cuenta.Id).ToList();
            foreach (var grupo in periodo.GroupBy(a => a.ConceptoId).OrderBy(g => g.Key))
            {
                var concepto = grupo.Key is Guid id ? conceptos.GetValueOrDefault(id) : null;
                // M-203: el traspaso entre cuentas propias no es ingreso ni egreso; solo mueve el saldo de cada cuenta (abajo, en `neto`).
                if (concepto?.ClasificacionFlujo == ClasificacionFlujo.TraspasoInterno) continue;
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
        await AgregarReclasificacionesAsync(filas, cuentas, ids, query, conceptos, cancellationToken);
        return new ReporteJsonResponse("Flujo de efectivo", clock.UtcNow,
            [new("Del", query.Desde.ToString("yyyy-MM-dd")), new("Al", query.Hasta.ToString("yyyy-MM-dd"))],
            [new("cuenta", "Cuenta", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
             new("clasificacion", "Clasificación", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
             new("concepto", "Concepto", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda),
             new("moneda", "Moneda", TipoColumnaReporte.Texto, AlineacionColumna.Centro),
             new("ingresos", "Ingresos", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
             new("egresos", "Egresos", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
             new("neto", "Saldo / neto", TipoColumnaReporte.Moneda, AlineacionColumna.Derecha),
             new("rastro", "Rastro de reclasificación", TipoColumnaReporte.Texto, AlineacionColumna.Izquierda)], filas, null);
    }

    static string Clasificacion(ConceptoMovimiento? c) => c?.ClasificacionFlujo switch
    {
        ClasificacionFlujo.Operacion => "Operación", ClasificacionFlujo.Inversion => "Inversión",
        ClasificacionFlujo.Financiamiento => "Financiamiento", _ => "Sin clasificar",
    };

    /// <summary>M-204: «Reclasificaciones del periodo»: las aprobadas de movimientos del periodo, con su rastro. No suman a los totales (el flujo ya usa el concepto vigente).</summary>
    private async Task AgregarReclasificacionesAsync(List<IReadOnlyDictionary<string, object?>> filas, IReadOnlyList<CuentaBancaria> cuentas,
        List<Guid> cuentaIds, FlujoEfectivoReporteQuery query, Dictionary<Guid, ConceptoMovimiento> conceptos, CancellationToken ct)
    {
        var aprobadas = await (from r in db.MovimientoReclasificaciones.AsNoTracking()
                               join m in FlujoLectura.Movimientos(db, cuentaIds) on r.MovimientoId equals m.Id
                               where r.Estado == EstadoReclasificacion.Aprobada && m.FechaValor >= query.Desde && m.FechaValor <= query.Hasta
                               orderby r.ResueltoEn
                               select new { r, m.CuentaBancariaId, m.Monto, m.Sentido, m.Moneda }).ToListAsync(ct);
        if (aprobadas.Count == 0) return;
        var usuarios = aprobadas.SelectMany(a => new[] { a.r.SolicitadoPor, a.r.ResueltoPor }).OfType<Guid>().Distinct().ToList();
        var nombresPorId = nombres is null ? new Dictionary<Guid, string>() : await nombres.NombresCajerosAsync(usuarios, ct);
        string Nombre(Guid? id) => id is Guid g && nombresPorId.TryGetValue(g, out var n) ? n : "(usuario)";
        string Etiqueta(Guid? id) => id is Guid g && conceptos.TryGetValue(g, out var c) ? $"{Clasificacion(c)} · {c.Nombre}" : "Sin clasificar";
        foreach (var a in aprobadas)
        {
            var cuenta = cuentas.First(c => c.Id == a.CuentaBancariaId);
            var ingreso = a.Sentido == SentidoMovimiento.Ingreso;
            filas.Add(new Dictionary<string, object?>
            {
                ["tipo"] = "reclasificacion", ["cuentaId"] = cuenta.Id, ["cuenta"] = $"{cuenta.Banco} {Clabe.Enmascarar(cuenta.NumeroCuenta)}",
                ["moneda"] = a.Moneda, ["clasificacion"] = "Reclasificaciones del periodo",
                ["concepto"] = $"{Etiqueta(a.r.ConceptoAnteriorId)} → {Etiqueta(a.r.ConceptoNuevoId)}",
                ["ingresos"] = ingreso ? a.Monto : null, ["egresos"] = ingreso ? null : a.Monto, ["neto"] = ingreso ? a.Monto : -a.Monto,
                ["rastro"] = $"Pidió {Nombre(a.r.SolicitadoPor)} · autorizó {Nombre(a.r.ResueltoPor)} el {a.r.ResueltoEn:dd/MM/yyyy} · {a.r.Motivo}",
            });
        }
    }
}

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

// ============================================================================
// TES-PR10: auxiliar de bancos por cuenta/período (§7 del levantamiento,
// TES-6) — reemplaza el export semanal de SAP. Libro cronológico con saldo
// acumulado. ⚠️ El acumulado arranca del neto de movimientos del sistema
// anteriores al período: los saldos iniciales reales por cuenta llegan con
// la conciliación (PR-9, gate T-G8).
// ============================================================================

public sealed record AuxiliarBancosReporteQuery(
    Guid CuentaBancariaId,
    DateOnly Desde,
    DateOnly Hasta) : IRequest<ReporteJsonResponse>;

public sealed class AuxiliarBancosReporteValidator : AbstractValidator<AuxiliarBancosReporteQuery>
{
    public AuxiliarBancosReporteValidator()
    {
        RuleFor(q => q.CuentaBancariaId).NotEmpty();
        RuleFor(q => q.Desde).NotEmpty();
        RuleFor(q => q.Hasta).NotEmpty().GreaterThanOrEqualTo(q => q.Desde);
    }
}

public sealed class AuxiliarBancosReporteHandler
    : IRequestHandler<AuxiliarBancosReporteQuery, ReporteJsonResponse>
{
    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;

    public AuxiliarBancosReporteHandler(TesoreriaDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task<ReporteJsonResponse> Handle(
        AuxiliarBancosReporteQuery query, CancellationToken cancellationToken)
    {
        var cuenta = await _db.CuentasBancarias.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.CuentaBancariaId, cancellationToken)
            ?? throw new EntityNotFoundException("CTA_NO_ENCONTRADA",
                $"No se encontró la cuenta bancaria '{query.CuentaBancariaId}'.");

        // Saldo de arranque = neto de movimientos del sistema previos al
        // período (sin saldo inicial bancario hasta T-G8/PR-9).
        var saldoInicial = await _db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.CuentaBancariaId == cuenta.Id && m.FechaValor < query.Desde)
            .SumAsync(m => (decimal?)(m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto),
                cancellationToken) ?? 0m;

        var movimientos = await _db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.CuentaBancariaId == cuenta.Id
                        && m.FechaValor >= query.Desde && m.FechaValor <= query.Hasta)
            .OrderBy(m => m.FechaValor).ThenBy(m => m.CreadoEn)
            .ToListAsync(cancellationToken);

        var conceptoIds = movimientos.Where(m => m.ConceptoId is not null)
            .Select(m => m.ConceptoId!.Value).Distinct().ToList();
        var conceptos = conceptoIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.ConceptosMovimiento.AsNoTracking()
                .Where(c => conceptoIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Nombre, cancellationToken);

        var filas = new List<IReadOnlyDictionary<string, object?>>(movimientos.Count);
        var saldo = saldoInicial;
        foreach (var m in movimientos)
        {
            var ingreso = m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : 0m;
            var egreso = m.Sentido == SentidoMovimiento.Egreso ? m.Monto : 0m;
            saldo += ingreso - egreso;
            filas.Add(new Dictionary<string, object?>
            {
                ["fecha"] = m.FechaValor,
                ["referencia"] = m.ReferenciaBancaria,
                ["concepto"] = m.ConceptoId is Guid cid && conceptos.TryGetValue(cid, out var n)
                    ? n
                    : m.MotivoNoAplicado is not null ? "Pago a cuenta" : null,
                ["ingreso"] = ingreso == 0m ? null : ingreso,
                ["egreso"] = egreso == 0m ? null : egreso,
                ["saldo"] = saldo,
                ["conciliado"] = m.EstadoConciliacion == EstadoConciliacionMovimiento.Conciliado,
            });
        }

        var totalIngresos = movimientos.Where(m => m.Sentido == SentidoMovimiento.Ingreso).Sum(m => m.Monto);
        var totalEgresos = movimientos.Where(m => m.Sentido == SentidoMovimiento.Egreso).Sum(m => m.Monto);

        return new ReporteJsonResponse(
            Titulo: $"Auxiliar de bancos — {cuenta.Banco} {Clabe.Enmascarar(cuenta.NumeroCuenta)} ({cuenta.Moneda})",
            GeneradoEn: _clock.UtcNow,
            FiltrosAplicados:
            [
                new("Cuenta", $"{cuenta.Banco} {Clabe.Enmascarar(cuenta.NumeroCuenta)}"),
                new("Del", query.Desde.ToString("yyyy-MM-dd")),
                new("Al", query.Hasta.ToString("yyyy-MM-dd")),
                new("Saldo previo (movimientos del sistema)", saldoInicial.ToString("N2")),
            ],
            Columnas:
            [
                new("fecha",      "Fecha",       TipoColumnaReporte.Fecha,    AlineacionColumna.Centro),
                new("referencia", "Referencia",  TipoColumnaReporte.Texto,    AlineacionColumna.Izquierda),
                new("concepto",   "Concepto",    TipoColumnaReporte.Texto,    AlineacionColumna.Izquierda),
                new("ingreso",    "Ingreso",     TipoColumnaReporte.Moneda,   AlineacionColumna.Derecha),
                new("egreso",     "Egreso",      TipoColumnaReporte.Moneda,   AlineacionColumna.Derecha),
                new("saldo",      "Saldo",       TipoColumnaReporte.Moneda,   AlineacionColumna.Derecha),
                new("conciliado", "Conciliado",  TipoColumnaReporte.Booleano, AlineacionColumna.Centro),
            ],
            Filas: filas,
            Totales: new Dictionary<string, object?>
            {
                ["ingreso"] = totalIngresos,
                ["egreso"] = totalEgresos,
                ["saldo"] = saldo,
            });
    }
}

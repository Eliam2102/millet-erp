using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Cobranza;

/// <summary>M-345: estado de una promesa de pago frente a los pagos aplicados a la factura de cartera.</summary>
public sealed record EvaluarPromesaFacturaQuery(
    Guid FacturaCarteraId, decimal MontoPrometido, DateOnly FechaPrometida, DateOnly Desde)
    : IRequest<ResultadoPromesa>;

public sealed class EvaluarPromesaFacturaHandler(CuentasPorCobrarDbContext db, IClock clock)
    : IRequestHandler<EvaluarPromesaFacturaQuery, ResultadoPromesa>
{
    public async Task<ResultadoPromesa> Handle(EvaluarPromesaFacturaQuery q, CancellationToken ct)
    {
        if (q.MontoPrometido <= 0)
            throw new BusinessRuleException("PRO_MONTO_INVALIDO", "El monto prometido debe ser mayor a cero.");
        if (q.FechaPrometida < q.Desde)
            throw new BusinessRuleException("PRO_FECHAS_INVALIDAS", "La fecha prometida no puede ser anterior a la promesa.");
        if (!await db.FacturasCartera.AnyAsync(f => f.Id == q.FacturaCarteraId, ct))
            throw new EntityNotFoundException("FACTURA_CARTERA_NO_ENCONTRADA", $"No existe la factura de cartera '{q.FacturaCarteraId}'.");

        var movimientos = await db.MovimientosCartera.AsNoTracking()
            .Where(m => m.FacturaCarteraId == q.FacturaCarteraId && m.Tipo == TipoMovimientoCartera.Pago && !m.Revertido)
            .Select(m => new { m.FechaMovimiento, m.Importe })
            .ToListAsync(ct);
        var pagos = movimientos.Select(m => new PagoAplicado(DateOnly.FromDateTime(m.FechaMovimiento.UtcDateTime), m.Importe));
        return CumplimientoPromesa.Evaluar(
            q.MontoPrometido, q.FechaPrometida, q.Desde, pagos, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
    }
}

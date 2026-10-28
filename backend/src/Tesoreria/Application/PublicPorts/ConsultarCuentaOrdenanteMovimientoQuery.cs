using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.PublicPorts;

/// <summary>
/// Puerto público de lectura (86e3tfdve, opción A): cuenta ordenante del cliente
/// capturada al registrar el ingreso, para la <c>CtaOrdenante</c> del REP. Devuelve
/// la cuenta completa: el consumidor la usa solo para el CFDI y la enmascara al
/// mostrarla (ADR-0006). Sustituye a llevarla en el evento de pago confirmado
/// (cuidados-infra §4).
/// </summary>
public sealed record ConsultarCuentaOrdenanteMovimientoQuery(Guid MovimientoBancarioId) : IRequest<string?>;

public sealed class ConsultarCuentaOrdenanteMovimientoHandler(TesoreriaDbContext db)
    : IRequestHandler<ConsultarCuentaOrdenanteMovimientoQuery, string?>
{
    public Task<string?> Handle(ConsultarCuentaOrdenanteMovimientoQuery q, CancellationToken cancellationToken) =>
        db.MovimientosBancarios.AsNoTracking()
            .Where(m => m.Id == q.MovimientoBancarioId)
            .Select(m => m.CuentaOrdenante)
            .FirstOrDefaultAsync(cancellationToken);
}

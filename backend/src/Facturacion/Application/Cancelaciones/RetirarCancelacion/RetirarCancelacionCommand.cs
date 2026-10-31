using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Cancelaciones.RetirarCancelacion;

/// <summary>
/// Ingresos retira una cancelación que aún no se autoriza (F6.5, propuesta de la
/// ficha del 30-oct a validar con Millet). El comprobante sigue vigente.
/// </summary>
public sealed record RetirarCancelacionCommand(Guid ComprobanteId) : IRequest<SolicitarCancelacionResponse>;

public sealed class RetirarCancelacionHandler(FacturacionDbContext db, IClock clock)
    : IRequestHandler<RetirarCancelacionCommand, SolicitarCancelacionResponse>
{
    public async Task<SolicitarCancelacionResponse> Handle(RetirarCancelacionCommand command, CancellationToken cancellationToken)
    {
        var solicitud = await CadenaCancelacion.PendienteDeAsync(db, command.ComprobanteId, cancellationToken);
        solicitud.Retirar(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        var comprobante = await db.Comprobantes.AsNoTracking().FirstAsync(c => c.Id == command.ComprobanteId, cancellationToken);
        return SolicitarCancelacionResponse.De(comprobante, solicitud);
    }
}

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
        // FAC-08b (supuesto a validar): la sustituta ya está timbrada; retirar dejaría dos facturas
        // vigentes. Si no procede, el Contador General la rechaza y la sustituta se cancela.
        if (await Sustituciones.SustitucionCfdi.SustitutaDeSolicitudAsync(db, solicitud, cancellationToken) is not null)
            throw new Millet.SharedKernel.Application.Exceptions.BusinessRuleException(
                "SUSTITUCION_NO_RETIRABLE",
                "La factura ya se sustituyó: la cancelación no se puede retirar. Si no procede, el Contador General debe rechazarla.");
        solicitud.Retirar(clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        var comprobante = await db.Comprobantes.AsNoTracking().FirstAsync(c => c.Id == command.ComprobanteId, cancellationToken);
        return SolicitarCancelacionResponse.De(comprobante, solicitud);
    }
}

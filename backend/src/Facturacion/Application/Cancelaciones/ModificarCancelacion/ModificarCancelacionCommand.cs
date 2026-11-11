using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones.ModificarCancelacion;

/// <summary>
/// M-235: quien solicitó la cancelación corrige el motivo SAT o el sustituto mientras sigue
/// pendiente. Si ya tenía la primera firma, se anula y hay que autorizar de nuevo.
/// </summary>
public sealed record ModificarCancelacionCommand(Guid ComprobanteId, string MotivoSat, string? UuidSustituto)
    : IRequest<SolicitarCancelacionResponse>;

public sealed class ModificarCancelacionHandler(FacturacionDbContext db, ICurrentUserContext user)
    : IRequestHandler<ModificarCancelacionCommand, SolicitarCancelacionResponse>
{
    public async Task<SolicitarCancelacionResponse> Handle(ModificarCancelacionCommand command, CancellationToken cancellationToken)
    {
        if (user.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se identificó al usuario que modifica.");

        var solicitud = await CadenaCancelacion.PendienteDeAsync(db, command.ComprobanteId, cancellationToken);
        // Con sustitución ya timbrada no se cambia el motivo (FAC-08b): la sustituta ya relaciona este CFDI.
        if (await Sustituciones.SustitucionCfdi.SustitutaDeSolicitudAsync(db, solicitud, cancellationToken) is not null)
            throw new BusinessRuleException(
                "SUSTITUCION_NO_MODIFICABLE",
                "La factura ya se sustituyó: la cancelación no se puede modificar. Si no procede, el Contador General debe rechazarla.");

        solicitud.Modificar(usuarioId, command.MotivoSat, command.UuidSustituto);
        await db.SaveChangesAsync(cancellationToken);

        var comprobante = await db.Comprobantes.AsNoTracking().FirstAsync(c => c.Id == command.ComprobanteId, cancellationToken);
        return SolicitarCancelacionResponse.De(comprobante, solicitud);
    }
}

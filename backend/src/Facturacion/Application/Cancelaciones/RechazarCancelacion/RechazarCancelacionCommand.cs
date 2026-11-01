using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones.RechazarCancelacion;

/// <summary>
/// El Contador General no autoriza una cancelación pendiente (F6.5): queda con su
/// motivo y el comprobante sigue vigente. Nada se envía al PAC.
/// </summary>
public sealed record RechazarCancelacionCommand(Guid ComprobanteId, string Motivo) : IRequest<SolicitarCancelacionResponse>;

public sealed class RechazarCancelacionValidator : AbstractValidator<RechazarCancelacionCommand>
{
    public RechazarCancelacionValidator()
    {
        RuleFor(c => c.ComprobanteId).NotEmpty();
        RuleFor(c => c.Motivo)
            .NotEmpty().WithMessage("Indica el motivo por el que no se autoriza la cancelación.")
            .MaximumLength(500).WithMessage("El motivo admite como máximo 500 caracteres.");
    }
}

public sealed class RechazarCancelacionHandler(FacturacionDbContext db, ICurrentUserContext user, IClock clock)
    : IRequestHandler<RechazarCancelacionCommand, SolicitarCancelacionResponse>
{
    public async Task<SolicitarCancelacionResponse> Handle(RechazarCancelacionCommand command, CancellationToken cancellationToken)
    {
        if (user.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se identificó al usuario que rechaza.");

        var solicitud = await CadenaCancelacion.PendienteDeAsync(db, command.ComprobanteId, cancellationToken);
        solicitud.RechazarAutorizacion(usuarioId, user.UserName, command.Motivo, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        var comprobante = await db.Comprobantes.AsNoTracking().FirstAsync(c => c.Id == command.ComprobanteId, cancellationToken);
        return SolicitarCancelacionResponse.De(comprobante, solicitud);
    }
}

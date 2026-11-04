using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;

/// <summary>
/// Decide la solicitud. Es solo una decisión interna: no cambia el CFDI ni el pasivo (aunque la
/// factura esté autorizada o pagada, la decisión se guarda). Si se acepta y después el SAT reporta
/// el CFDI como Cancelado, la sincronización (<c>FiscalCfdiReceiverAdapter</c>) cancela el pasivo
/// o lo manda a revisión; si el SAT sigue diciendo Vigente, no pasa nada.
/// </summary>
public sealed record DecidirSolicitudCancelacionCommand(
    Guid CfdiId,
    Guid SolicitudId,
    bool Aceptar,
    string Comentario,
    string? EvidenciaBlobRef,
    string? EvidenciaNombre) : IRequest<SolicitudCancelacionResponse>;

public sealed class DecidirSolicitudCancelacionValidator : AbstractValidator<DecidirSolicitudCancelacionCommand>
{
    public DecidirSolicitudCancelacionValidator()
    {
        RuleFor(c => c.CfdiId).NotEmpty();
        RuleFor(c => c.SolicitudId).NotEmpty();
        RuleFor(c => c.Comentario).NotEmpty().WithErrorCode("SOLICITUD_CANCELACION_EVIDENCIA_VACIA")
            .WithMessage("La decisión requiere un comentario de evidencia.").MaximumLength(1000);
        RuleFor(c => c.EvidenciaBlobRef).MaximumLength(400);
        RuleFor(c => c.EvidenciaNombre).MaximumLength(255);
    }
}

public sealed class DecidirSolicitudCancelacionHandler
    : IRequestHandler<DecidirSolicitudCancelacionCommand, SolicitudCancelacionResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public DecidirSolicitudCancelacionHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<SolicitudCancelacionResponse> Handle(
        DecidirSolicitudCancelacionCommand command, CancellationToken cancellationToken)
    {
        var solicitud = await _db.SolicitudesCancelacionCfdi
            .FirstOrDefaultAsync(s => s.Id == command.SolicitudId && s.CfdiRecibidoId == command.CfdiId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SOLICITUD_CANCELACION_NO_ENCONTRADA", $"No existe la solicitud '{command.SolicitudId}' para ese CFDI.");

        var ahora = _clock.UtcNow;
        solicitud.Decidir(command.Aceptar, command.Comentario, command.EvidenciaBlobRef, command.EvidenciaNombre,
            _currentUser.UserId, ahora);

        await _db.SaveChangesAsync(cancellationToken);
        return SolicitudCancelacionResponse.De(solicitud);
    }
}

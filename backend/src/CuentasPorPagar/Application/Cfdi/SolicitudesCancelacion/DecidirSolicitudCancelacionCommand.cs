using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CancelarFactura;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;

/// <summary>
/// Decide la solicitud. Aceptar: el CFDI queda Cancelado ante el SAT y, si ya era
/// pasivo (factura), se cancela con <see cref="CancelarFacturaCommand"/> (publica
/// <c>factura.cancelada.v1</c> para Compras y Contabilidad) en la misma transacción.
/// Rechazar: solo queda el registro con su evidencia; el CFDI sigue vigente.
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
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public DecidirSolicitudCancelacionHandler(
        CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IMediator mediator, IClock clock)
    {
        _db = db; _currentUser = currentUser; _mediator = mediator; _clock = clock;
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

        if (command.Aceptar)
        {
            var cfdi = await _db.CfdisRecibidos.FirstAsync(c => c.Id == command.CfdiId, cancellationToken);
            cfdi.ActualizarEstatusSat(EstatusSatCfdi.Cancelado, ahora);

            // Sin pasivo (PorProcesar) basta el estatus SAT. NC y anticipo no se cancelan aquí (ver PR).
            var factura = cfdi.Estado == EstadoCfdiRecibido.ConvertidoEnPasivo
                ? await _db.FacturasProveedor.FirstOrDefaultAsync(
                    f => f.CfdiRecibidoId == cfdi.Id && f.Estado != EstadoPasivo.Cancelada, cancellationToken)
                : null;
            if (factura is not null)
            {
                // Guarda también la solicitud y el CFDI: misma transacción; si falla la cancelación no se guarda nada.
                await _mediator.Send(new CancelarFacturaCommand(
                    factura.Id, factura.Version, MotivoCancelacion.CfdiCanceladoEnSat,
                    $"Solicitud de cancelación {solicitud.Id} aceptada"), cancellationToken);
                return SolicitudCancelacionResponse.De(solicitud);
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return SolicitudCancelacionResponse.De(solicitud);
    }
}

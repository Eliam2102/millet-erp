using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Movimientos;

// M-204: reclasificar es una solicitud; el concepto cambia hasta que otra persona con la facultad la autoriza.

public sealed record ReclasificarMovimientoCommand(Guid MovimientoId, Guid ConceptoId, string Motivo, int VersionEsperada) : IRequest;
public sealed class ReclasificarMovimientoValidator : AbstractValidator<ReclasificarMovimientoCommand>
{
    public ReclasificarMovimientoValidator()
    {
        RuleFor(x => x.MovimientoId).NotEmpty();
        RuleFor(x => x.ConceptoId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().MaximumLength(400);
    }
}
public sealed class ReclasificarMovimientoHandler(TesoreriaDbContext db, ICurrentUserContext currentUser, IClock clock)
    : IRequestHandler<ReclasificarMovimientoCommand>
{
    public async Task Handle(ReclasificarMovimientoCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que solicita la reclasificación.");
        var movimiento = await db.MovimientosBancarios.FirstOrDefaultAsync(m => m.Id == command.MovimientoId, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO", "No se encontró el movimiento.");
        if (movimiento.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(movimiento), movimiento.Id);
        if (!await db.ConceptosMovimiento.AnyAsync(c => c.Id == command.ConceptoId && c.Activo, cancellationToken))
            throw new BusinessRuleException("MOV_CONCEPTO_INVALIDO", "Selecciona un concepto activo.");
        if (await db.MovimientoReclasificaciones.AnyAsync(
                r => r.MovimientoId == movimiento.Id && r.Estado == EstadoReclasificacion.Pendiente, cancellationToken))
            throw new BusinessRuleException("MOV_RECLASIFICACION_PENDIENTE", "El movimiento ya tiene una reclasificación pendiente de autorizar.");
        db.MovimientoReclasificaciones.Add(MovimientoReclasificacion.Solicitar(
            movimiento.Id, movimiento.ConceptoId, command.ConceptoId, command.Motivo, usuarioId, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }
}

public sealed record AutorizarReclasificacionCommand(Guid MovimientoId) : IRequest;
public sealed class AutorizarReclasificacionHandler(TesoreriaDbContext db, ICurrentUserContext currentUser, IClock clock)
    : IRequestHandler<AutorizarReclasificacionCommand>
{
    public async Task Handle(AutorizarReclasificacionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que autoriza.");
        var (movimiento, solicitud) = await CargarAsync(db, command.MovimientoId, cancellationToken);
        solicitud.Aprobar(usuarioId, clock.UtcNow);
        movimiento.Reclasificar(solicitud.ConceptoNuevoId, solicitud.Motivo);
        await db.SaveChangesAsync(cancellationToken);
    }

    internal static async Task<(MovimientoBancario, MovimientoReclasificacion)> CargarAsync(
        TesoreriaDbContext db, Guid movimientoId, CancellationToken ct)
    {
        var movimiento = await db.MovimientosBancarios.FirstOrDefaultAsync(m => m.Id == movimientoId, ct)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO", "No se encontró el movimiento.");
        var solicitud = await db.MovimientoReclasificaciones
            .FirstOrDefaultAsync(r => r.MovimientoId == movimientoId && r.Estado == EstadoReclasificacion.Pendiente, ct)
            ?? throw new BusinessRuleException("MOV_RECLASIFICACION_SIN_PENDIENTE", "El movimiento no tiene una reclasificación pendiente.");
        return (movimiento, solicitud);
    }
}

public sealed record RechazarReclasificacionCommand(Guid MovimientoId, string Motivo) : IRequest;
public sealed class RechazarReclasificacionValidator : AbstractValidator<RechazarReclasificacionCommand>
{
    public RechazarReclasificacionValidator()
    {
        RuleFor(x => x.MovimientoId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().MaximumLength(400);
    }
}
public sealed class RechazarReclasificacionHandler(TesoreriaDbContext db, ICurrentUserContext currentUser, IClock clock)
    : IRequestHandler<RechazarReclasificacionCommand>
{
    public async Task Handle(RechazarReclasificacionCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid usuarioId)
            throw new ForbiddenException("USUARIO_NO_IDENTIFICADO", "No se pudo identificar al usuario que rechaza.");
        var (_, solicitud) = await AutorizarReclasificacionHandler.CargarAsync(db, command.MovimientoId, cancellationToken);
        solicitud.Rechazar(usuarioId, command.Motivo, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}

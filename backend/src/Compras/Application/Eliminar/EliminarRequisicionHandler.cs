using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Eliminar;

/// <summary>
/// Handler de <see cref="EliminarRequisicionCommand"/>:
/// <list type="number">
///   <item>Carga la requisición.</item>
///   <item>Valida el motivo cross-table: activo + bitmask
///         <c>aplica_a</c> incluye <c>Eliminacion</c> + exige texto cuando
///         <c>permite_texto_libre</c>.</item>
///   <item>Invoca <c>Eliminar</c> en el agregado, que valida estado
///         (<c>Borrador</c> o <c>EnAutorizacion</c>) y aplica la
///         terminación.</item>
/// </list>
/// El actor viene del JWT; nunca del request.
/// </summary>
public sealed class EliminarRequisicionHandler : IRequestHandler<EliminarRequisicionCommand, Unit>
{
    private readonly ComprasDbContext _db;
    private readonly IMediator _mediator;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public EliminarRequisicionHandler(
        ComprasDbContext db,
        IMediator mediator,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db;
        _mediator = mediator;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Unit> Handle(EliminarRequisicionCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid actorId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        var requisicion = await _db.Requisiciones
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        var motivo = await _db.MotivosRechazo
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == command.MotivoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "MOTIVO_NO_ENCONTRADO",
                $"No se encontró motivo de rechazo con id '{command.MotivoId}'.");

        if (!motivo.Activo)
        {
            throw new BusinessRuleException(
                "MOTIVO_INACTIVO",
                $"El motivo '{motivo.Clave}' está inactivo y no puede usarse.");
        }

        if ((motivo.AplicaA & MotivoRechazoAplicaA.Eliminacion) == 0)
        {
            throw new BusinessRuleException(
                "MOTIVO_NO_APLICA_A_ELIMINACION",
                $"El motivo '{motivo.Clave}' no aplica al flujo de eliminación.");
        }

        if (motivo.PermiteTextoLibre && string.IsNullOrWhiteSpace(command.MotivoTexto))
        {
            throw new BusinessRuleException(
                "MOTIVO_TEXTO_REQUERIDO",
                $"El motivo '{motivo.Clave}' requiere texto libre que describa la razón.");
        }

        var evento = requisicion.Eliminar(
            motivoId: command.MotivoId,
            actorId: actorId,
            fechaHora: _clock.UtcNow,
            motivoTexto: command.MotivoTexto);

        // F6-PR3: publicar antes del SaveChanges (mismo patrón que
        // Rechazar) para que el outbox se popule en la misma TX.
        await _mediator.Publish(evento, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

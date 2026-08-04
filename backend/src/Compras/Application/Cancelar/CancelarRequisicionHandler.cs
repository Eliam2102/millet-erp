using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Serilog.Context;

namespace Millet.Compras.Application.Cancelar;

/// <summary>
/// Handler de <see cref="CancelarRequisicionCommand"/> (F4-PR3):
/// <list type="number">
///   <item>Carga la requisición con sus líneas.</item>
///   <item>Valida el motivo cross-table (activo + bitmask
///         <c>aplica_a</c> incluye <c>Cancelacion</c> + texto si
///         <c>permite_texto_libre</c>).</item>
///   <item>Abre TX EF.</item>
///   <item>Invoca <c>requisicion.Cancelar(...)</c> en el agregado
///         (transición a <c>Cancelada</c> + evento).</item>
///   <item>(PR4/ADR-0047) Las RQ ya no reservan stock: no hay reservas que liberar.</item>
///   <item>Borra las filas de <c>oc_borrador_stub</c> con
///         <c>OrigenRequisicionId == requisicionId</c> (aborta OC
///         borrador in-proc).</item>
///   <item>SaveChanges + Commit.</item>
///   <item>Publica <see cref="Domain.Events.RequisicionCanceladaEvent"/>
///         post-commit (informativo; F4-PR4 wirea handlers).</item>
/// </list>
///
/// <para>
/// Si cualquier puerto/operación lanza, el using-dispose de la TX
/// rollbackea: la RQ regresa al estado previo (Autorizada o EnSurtido)
/// y las reservas/OC quedan intactas. La excepción se traduce a
/// <see cref="BusinessRuleException"/> con código <c>CANCELAR_FALLO</c>
/// → 422 (mismo patrón que F4-PR2 BIFURCACION_FALLO).
/// </para>
/// </summary>
public sealed class CancelarRequisicionHandler : IRequestHandler<CancelarRequisicionCommand, Unit>
{
    private readonly ComprasDbContext _db;
    private readonly IMediator _mediator;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public CancelarRequisicionHandler(
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

    public async Task<Unit> Handle(CancelarRequisicionCommand command, CancellationToken cancellationToken)
    {
        // F8-PR2: instrumentation por consistencia con AutorizarHandler.
        using var _ = LogContext.PushProperty("RequisicionId", command.RequisicionId);
        using var activity = ComprasActivitySource.Instance.StartActivity("Compras.Cancelar");
        activity?.SetTag("compras.requisicion.id", command.RequisicionId);

        if (_currentUser.UserId is not Guid actorId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        var requisicion = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        activity?.SetTag("compras.estado.antes", requisicion.Estado.ToString());

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

        if ((motivo.AplicaA & MotivoRechazoAplicaA.Cancelacion) == 0)
        {
            throw new BusinessRuleException(
                "MOTIVO_NO_APLICA_A_CANCELACION",
                $"El motivo '{motivo.Clave}' no aplica al flujo de cancelación.");
        }

        if (motivo.PermiteTextoLibre && string.IsNullOrWhiteSpace(command.MotivoTexto))
        {
            throw new BusinessRuleException(
                "MOTIVO_TEXTO_REQUERIDO",
                $"El motivo '{motivo.Clave}' requiere texto libre que describa la razón.");
        }

        try
        {
            await EjecutarCancelacionAsync(requisicion, command, actorId, cancellationToken);
        }
        catch (Exception ex) when (ex is not BusinessRuleException && ex is not OperationCanceledException)
        {
            throw new BusinessRuleException(
                "CANCELAR_FALLO",
                $"La cancelación falló: {ex.Message}",
                ex);
        }

        activity?.SetTag("compras.estado.despues", requisicion.Estado.ToString());
        return Unit.Value;
    }

    private async Task EjecutarCancelacionAsync(
        Requisicion requisicion,
        CancelarRequisicionCommand command,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // 1. Transición de estado + emite evento.
        var evento = requisicion.Cancelar(
            motivoId: command.MotivoId,
            actorId: actorId,
            fechaHora: _clock.UtcNow,
            motivoTexto: command.MotivoTexto);

        // 2. (PR4 / ADR-0047) Las RQ ya no reservan stock → nada que liberar.

        // 3. Abortar OC borrador in-proc: delete filas con origen = rqId
        // de oc_borrador_stub. (Cuando exista OC real en submódulo, esto
        // se reemplazará por un puerto IAbortarSolicitudCompraPort.)
        await _db.OcBorradorStubs
            .Where(s => s.OrigenRequisicionId == requisicion.Id)
            .ExecuteDeleteAsync(cancellationToken);

        // 4. F6-PR3: publicar el domain event antes del SaveChanges
        // final para que el integration mapper inserte la fila a outbox
        // dentro de la misma TX (atomicidad).
        await _mediator.Publish(evento, cancellationToken);

        // 5. Persistir cambios (estado + campos terminación + outbox row).
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }
}

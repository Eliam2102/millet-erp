using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Serilog.Context;

namespace Millet.Compras.Application.CerrarManual;

/// <summary>
/// Handler de <see cref="CerrarManualRequisicionCommand"/> (ADR-0043 R3),
/// espejo de <c>CancelarRequisicionHandler</c>:
/// <list type="number">
///   <item>Carga la requisición con sus líneas.</item>
///   <item>Valida el motivo cross-table (activo + bitmask <c>aplica_a</c>
///         incluye <c>CierreManual</c> + texto si <c>permite_texto_libre</c>).</item>
///   <item>Abre TX EF.</item>
///   <item>Invoca <c>requisicion.CerrarManual(...)</c> (el agregado deriva el
///         terminal de lo entregado y emite el evento).</item>
///   <item>(PR4/ADR-0047) Las RQ ya no reservan stock: no hay reservas que liberar;
///         todo (almacén y compra) queda como stock libre.</item>
///   <item>Publica el evento antes de SaveChanges (outbox en la misma TX) +
///         SaveChanges + Commit.</item>
/// </list>
///
/// <para>
/// <b>No aborta OCs.</b> A diferencia de la cancelación, el cierre manual deja
/// que el material pedido en vuelo llegue como stock (ADR-0043: permitir con
/// aviso). Si cualquier puerto falla, el dispose de la TX hace rollback y la
/// excepción se traduce a <c>CIERRE_MANUAL_FALLO</c> → 422.
/// </para>
/// </summary>
public sealed class CerrarManualRequisicionHandler : IRequestHandler<CerrarManualRequisicionCommand, Unit>
{
    private readonly ComprasDbContext _db;
    private readonly IMediator _mediator;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public CerrarManualRequisicionHandler(
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

    public async Task<Unit> Handle(CerrarManualRequisicionCommand command, CancellationToken cancellationToken)
    {
        using var _ = LogContext.PushProperty("RequisicionId", command.RequisicionId);
        using var activity = ComprasActivitySource.Instance.StartActivity("Compras.CerrarManual");
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

        if ((motivo.AplicaA & MotivoRechazoAplicaA.CierreManual) == 0)
        {
            throw new BusinessRuleException(
                "MOTIVO_NO_APLICA_A_CIERRE_MANUAL",
                $"El motivo '{motivo.Clave}' no aplica al flujo de cierre manual.");
        }

        if (motivo.PermiteTextoLibre && string.IsNullOrWhiteSpace(command.MotivoTexto))
        {
            throw new BusinessRuleException(
                "MOTIVO_TEXTO_REQUERIDO",
                $"El motivo '{motivo.Clave}' requiere texto libre que describa la razón.");
        }

        try
        {
            await EjecutarCierreAsync(requisicion, command, actorId, cancellationToken);
        }
        catch (Exception ex) when (ex is not BusinessRuleException && ex is not OperationCanceledException)
        {
            throw new BusinessRuleException(
                "CIERRE_MANUAL_FALLO",
                $"El cierre manual falló: {ex.Message}",
                ex);
        }

        activity?.SetTag("compras.estado.despues", requisicion.Estado.ToString());
        return Unit.Value;
    }

    private async Task EjecutarCierreAsync(
        Requisicion requisicion,
        CerrarManualRequisicionCommand command,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // 1. Transición (deriva el terminal de lo entregado) + emite evento.
        var evento = requisicion.CerrarManual(
            motivoId: command.MotivoId,
            actorId: actorId,
            fechaHora: _clock.UtcNow,
            motivoTexto: command.MotivoTexto);

        // 2. (PR4 / ADR-0047) Las RQ ya no reservan stock → nada que liberar;
        // todo queda como stock libre.

        // 3. NO se abortan OCs: el material pedido en vuelo llega como stock
        // (ADR-0043, permitir con aviso). El aviso de "OC en vuelo" es FE-side.

        // 4. Publicar el evento antes del SaveChanges para que el integration
        // mapper inserte la fila a outbox en la misma TX (atomicidad).
        await _mediator.Publish(evento, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }
}

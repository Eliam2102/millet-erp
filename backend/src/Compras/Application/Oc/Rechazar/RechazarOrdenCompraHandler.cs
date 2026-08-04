using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Rechazar;

/// <summary>
/// Rechaza una OC. Lookup + valida cross-table que el motivo:
/// <list type="bullet">
///   <item>Existe.</item>
///   <item>Está activo.</item>
///   <item>Aplica al flujo de OC (bitmask
///         <see cref="MotivoRechazoAplicaA.OrdenCompra"/>).</item>
///   <item>Si <c>PermiteTextoLibre</c> es true, el comando trae
///         <see cref="RechazarOrdenCompraCommand.MotivoRechazoTexto"/>
///         no vacío.</item>
/// </list>
/// Si pasa, invoca <see cref="OrdenCompra.Rechazar"/> y publica el evento.
/// </summary>
public sealed class RechazarOrdenCompraHandler : IRequestHandler<RechazarOrdenCompraCommand>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public RechazarOrdenCompraHandler(
        ComprasDbContext db,
        ICurrentUserContext currentUser,
        IClock clock,
        IPublisher publisher)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _publisher = publisher;
    }

    public async Task Handle(RechazarOrdenCompraCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        var oc = await _db.OrdenesCompra
            .Include(o => o.Autorizaciones)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        var motivo = await _db.MotivosRechazo
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == command.MotivoRechazoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "MOTIVO_RECHAZO_NO_ENCONTRADO",
                $"No se encontró motivo de rechazo con id '{command.MotivoRechazoId}'.");

        if (!motivo.Activo)
        {
            throw new BusinessRuleException(
                "MOTIVO_RECHAZO_INACTIVO",
                $"El motivo '{motivo.Clave}' está inactivo.");
        }

        if (!motivo.AplicaA.HasFlag(MotivoRechazoAplicaA.OrdenCompra))
        {
            throw new BusinessRuleException(
                "MOTIVO_RECHAZO_NO_APLICA_OC",
                $"El motivo '{motivo.Clave}' no aplica al rechazo de órdenes de compra.");
        }

        if (motivo.PermiteTextoLibre && string.IsNullOrWhiteSpace(command.MotivoRechazoTexto))
        {
            throw new BusinessRuleException(
                "MOTIVO_RECHAZO_TEXTO_REQUERIDO",
                $"El motivo '{motivo.Clave}' permite texto libre y requiere descripción adicional.");
        }

        var evento = oc.Rechazar(
            autorizacionId: Guid.CreateVersion7(),
            usuarioId: userId,
            fechaHora: _clock.UtcNow,
            motivoRechazoId: command.MotivoRechazoId,
            motivoRechazoTexto: command.MotivoRechazoTexto,
            notas: command.Notas);

        await _db.SaveChangesAsync(cancellationToken);
        await _publisher.Publish(evento, cancellationToken);
    }
}

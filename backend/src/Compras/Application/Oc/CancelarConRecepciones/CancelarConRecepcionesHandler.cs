using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.CancelarConRecepciones;

/// <summary>Primera firma: registra la solicitud y bloquea nuevas recepciones y facturas.</summary>
public sealed class CancelarConRecepcionesHandler : IRequestHandler<CancelarConRecepcionesCommand>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public CancelarConRecepcionesHandler(
        ComprasDbContext db,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task Handle(CancelarConRecepcionesCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        var motivo = await _db.MotivosRechazo
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == command.MotivoCancelacionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "MOTIVO_CANCELACION_NO_ENCONTRADO",
                $"No se encontró motivo con id '{command.MotivoCancelacionId}'.");

        if (!motivo.Activo)
        {
            throw new BusinessRuleException(
                "MOTIVO_CANCELACION_INACTIVO",
                $"El motivo '{motivo.Clave}' está inactivo.");
        }

        if (!motivo.AplicaA.HasFlag(MotivoRechazoAplicaA.Cancelacion))
        {
            throw new BusinessRuleException(
                "MOTIVO_NO_APLICA_CANCELACION",
                $"El motivo '{motivo.Clave}' no aplica al flujo de cancelación.");
        }

        if (motivo.PermiteTextoLibre && string.IsNullOrWhiteSpace(command.MotivoCancelacionTexto))
        {
            throw new BusinessRuleException(
                "MOTIVO_CANCELACION_TEXTO_REQUERIDO",
                $"El motivo '{motivo.Clave}' permite texto libre y requiere descripción adicional.");
        }

        oc.SolicitarCancelacionConRecepciones(userId, _clock.UtcNow,
            command.MotivoCancelacionId, command.MotivoCancelacionTexto ?? "");
        await _db.SaveChangesAsync(cancellationToken);
    }
}

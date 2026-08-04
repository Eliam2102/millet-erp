using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.CancelarConRecepciones;

/// <summary>
/// Handler para cancelar una OC con recepciones parciales (F5-PR4).
/// Lookup + validación cross-table del motivo + invocación del agregado
/// + liberación parcial proporcional por línea con RQ asociada.
///
/// <para>
/// La validación de los 3 permisos requeridos
/// (<c>compras.ordenes.cancelar-doble</c> + autorizar-nivel1 + autorizar-nivel2)
/// la hace el endpoint API antes de invocar este handler.
/// </para>
/// </summary>
public sealed class CancelarConRecepcionesHandler : IRequestHandler<CancelarConRecepcionesCommand>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public CancelarConRecepcionesHandler(
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

        var resultado = oc.CancelarConRecepcionesParciales(
            usuarioId: userId,
            fechaHora: _clock.UtcNow,
            motivoCancelacionId: command.MotivoCancelacionId,
            motivoCancelacionTexto: command.MotivoCancelacionTexto);

        // Liberar RQs: cada RQ con al menos una línea parcialmente
        // liberada deja de estar comprometida. Líneas totalmente
        // recibidas no liberan su RQ (queda comprometida históricamente
        // con esta OC cancelada).
        var rqsALiberar = resultado.LiberacionesParciales
            .Select(l => l.RequisicionId)
            .Distinct()
            .ToList();

        if (rqsALiberar.Count > 0)
        {
            var rqs = await _db.Requisiciones
                .Where(r => rqsALiberar.Contains(r.Id))
                .ToListAsync(cancellationToken);

            foreach (var rq in rqs)
            {
                rq.LiberarDeOc();
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _publisher.Publish(resultado.EventoCancelada, cancellationToken);

        foreach (var liberacion in resultado.LiberacionesParciales)
        {
            await _publisher.Publish(
                new LineaRqLiberadaEvent(
                    RequisicionId: liberacion.RequisicionId,
                    OrdenCompraId: oc.Id,
                    EmpresaId: oc.EmpresaId,
                    CantidadLiberada: liberacion.CantidadLiberada,
                    OcurridoEn: _clock.UtcNow),
                cancellationToken);
        }
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Serilog.Context;

namespace Millet.Compras.Application.Oc.Cancelar;

/// <summary>
/// Cancela una OC sin recepciones. Lookup + validación cross-table del
/// motivo (existe + activo + aplica a Cancelación bitmask + texto si
/// permite-texto). Si pasa, invoca el agregado y publica el evento.
///
/// F4-PR3: el agregado devuelve <see cref="Domain.Oc.CancelarResultado"/>
/// con la lista de RQs únicas a liberar. Cada RQ se libera en la misma
/// TX y se publica un <see cref="LineaRqLiberadaEvent"/> post-commit por
/// cada liberación.
/// </summary>
public sealed class CancelarOrdenCompraHandler : IRequestHandler<CancelarOrdenCompraCommand>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public CancelarOrdenCompraHandler(
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

    public async Task Handle(CancelarOrdenCompraCommand command, CancellationToken cancellationToken)
    {
        using var _ocId = LogContext.PushProperty("OrdenCompraId", command.OrdenCompraId);
        using var activity = ComprasActivitySource.Instance.StartActivity("Compras.CancelarOc");
        activity?.SetTag("compras.oc.id", command.OrdenCompraId);

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

        var resultado = oc.Cancelar(
            usuarioId: userId,
            fechaHora: _clock.UtcNow,
            motivoCancelacionId: command.MotivoCancelacionId,
            motivoCancelacionTexto: command.MotivoCancelacionTexto);

        if (resultado.RequisicionesALiberar.Count > 0)
        {
            var rqs = await _db.Requisiciones
                .Where(r => resultado.RequisicionesALiberar.Contains(r.Id))
                .ToListAsync(cancellationToken);

            foreach (var rq in rqs)
            {
                rq.LiberarDeOc();
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _publisher.Publish(resultado.EventoCancelada, cancellationToken);

        foreach (var rqId in resultado.RequisicionesALiberar)
        {
            await _publisher.Publish(
                new LineaRqLiberadaEvent(
                    RequisicionId: rqId,
                    OrdenCompraId: oc.Id,
                    EmpresaId: oc.EmpresaId,
                    CantidadLiberada: null,
                    OcurridoEn: _clock.UtcNow),
                cancellationToken);
        }
    }
}

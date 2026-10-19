using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// T9.6 · CA6.17: Facturación solicita el reembolso de una NC timbrada →
// Tesorería crea la DevolucionCliente de origen NC en estado Solicitada.
// Idempotente por EventoProcesado y por NotaCreditoId (índice único
// ux_devolucion_cliente_nota_credito de respaldo): una NC solo se reembolsa una vez.
// ============================================================================

public sealed record ProyectarReembolsoNotaCreditoCommand(
    Guid EventoId,
    NotaCreditoReembolsoSolicitadoPayload Payload) : IRequest;

public sealed class ProyectarReembolsoNotaCreditoHandler : IRequestHandler<ProyectarReembolsoNotaCreditoCommand>
{
    public const string EventType = NotaCreditoReembolsoSolicitadoPayload.EventType;

    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;

    public ProyectarReembolsoNotaCreditoHandler(TesoreriaDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task Handle(ProyectarReembolsoNotaCreditoCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;

        var existente = await _db.DevolucionesCliente
            .AsNoTracking()
            .AnyAsync(d => d.NotaCreditoId == p.NotaCreditoId, cancellationToken);

        if (!existente)
        {
            // Si el importe excede el total de la NC el dominio lanza DEV_IMPORTE_EXCEDE_NC: el worker
            // abandona el mensaje, se reintenta hasta maxDeliveryCount y termina en la DLQ para revisión.
            var folioDevolucion = await Devoluciones.DevolucionLectura.SiguienteFolioAsync(_db, p.EmpresaId, cancellationToken);
            _db.DevolucionesCliente.Add(DevolucionCliente.SolicitarPorNotaCredito(
                p.EmpresaId, folioDevolucion, p.NotaCreditoId, p.Folio, p.Uuid, p.ClienteId, p.Importe, p.TotalNotaCredito,
                p.Moneda, p.SucursalId, p.Motivo, p.SolicitadoPor, ahora));
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, EventType, ahora,
            detalle: $"nc={p.NotaCreditoId} importe={p.Importe} {p.Moneda}"));

        await _db.SaveChangesAsync(cancellationToken);
    }
}

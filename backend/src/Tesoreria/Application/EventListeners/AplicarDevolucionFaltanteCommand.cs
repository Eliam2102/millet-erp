using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.Depositos;
using Millet.Tesoreria.Application.Integration;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// 86e3vr1tj (junta 19-oct): el cajero devuelve el faltante en un corte posterior
// («devolución del corte de tal fecha»). Facturación registra el depósito en Mi caja y
// publica facturacion.caja-faltante.devuelto.v1; aquí se abona a la diferencia. Si la
// diferencia aún no está autorizada solo se acumula (no hay deudor que abonar todavía):
// el abono sale al autorizar (AutorizarDiferenciaCorteHandler).
// ============================================================================

public sealed record AplicarDevolucionFaltanteCommand(
    Guid EventoId,
    CajaFaltanteDevueltoPayload Payload) : IRequest;

public sealed class AplicarDevolucionFaltanteHandler : IRequestHandler<AplicarDevolucionFaltanteCommand>
{
    public const string EventType = CajaFaltanteDevueltoPayload.EventType;

    private readonly TesoreriaDbContext _db;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IClock _clock;

    public AplicarDevolucionFaltanteHandler(TesoreriaDbContext db, IIntegrationEventPublisher publisher, IClock clock)
    {
        _db = db; _publisher = publisher; _clock = clock;
    }

    public async Task Handle(AplicarDevolucionFaltanteCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;

        // Si la diferencia aún no existe (carrera con el cierre) la excepción deja el mensaje para retry.
        var dif = await _db.DiferenciasCorte.FirstOrDefaultAsync(d => d.CajaSesionId == p.CorteOrigenSesionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Aún no existe la diferencia del corte {p.CorteOrigenSesionId}; se reintenta.");

        var autorizada = dif.Autorizada;
        dif.AplicarDevolucion(p.Importe);
        if (autorizada)
            await _publisher.PublishAsync(DiferenciaCorteEventos.Devolucion(dif, p.Importe, p.CajaMovimientoId, ahora), cancellationToken);

        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, EventType, ahora,
            detalle: $"diferencia={dif.Id} corteOrigen={p.CorteOrigenSesionId} devuelto={p.Importe}"));
        await _db.SaveChangesAsync(cancellationToken);
    }
}

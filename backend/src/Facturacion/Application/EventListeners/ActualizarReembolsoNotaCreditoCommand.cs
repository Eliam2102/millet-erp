using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Eventos;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.EventListeners;

/// <summary>
/// F6.3: Tesorería reporta el estado del reembolso de una NC; la NC lo muestra (solicitado, autorizado, rechazado,
/// cancelado o pagado con fecha y referencia). Idempotente por <see cref="EventoProcesado"/>; un estado repetido o que
/// llega fuera de orden no retrocede el de la NC.
/// </summary>
public sealed record ActualizarReembolsoNotaCreditoCommand(Guid EventoId, ReembolsoNotaCreditoActualizadoPayload Payload) : IRequest;

public sealed class ActualizarReembolsoNotaCreditoHandler(FacturacionDbContext db, IClock clock)
    : IRequestHandler<ActualizarReembolsoNotaCreditoCommand>
{
    public const string EventType = ReembolsoNotaCreditoActualizadoPayload.EventType;

    public async Task Handle(ActualizarReembolsoNotaCreditoCommand command, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AnyAsync(e => e.EventoId == command.EventoId && e.EventoTipo == EventType, cancellationToken))
            return;
        var p = command.Payload;
        var estado = p.Estado switch
        {
            "Autorizada" => EstadoReembolsoNotaCredito.Autorizado,
            "Rechazada" => EstadoReembolsoNotaCredito.Rechazado,
            "Cancelada" => EstadoReembolsoNotaCredito.Cancelado,
            "Pagada" => EstadoReembolsoNotaCredito.Pagado,
            _ => (EstadoReembolsoNotaCredito?)null,
        };

        // Una NC que no existe aquí o un estado que no es de resolución (p. ej. «Solicitada») solo se registra.
        var nc = estado is null ? null
            : await db.NotasCredito.FirstOrDefaultAsync(n => n.Id == p.NotaCreditoId, cancellationToken);
        nc?.RegistrarEstadoReembolso(estado!.Value, p.OcurridoEn, p.FechaPago, p.ReferenciaBancaria, p.Motivo);

        db.EventosProcesados.Add(new EventoProcesado(command.EventoId, EventType, clock.UtcNow,
            $"reembolso NC; nc={p.NotaCreditoId} devolucion={p.DevolucionFolio} estado={p.Estado}{(nc is null ? " (sin NC)" : "")}"));
        await db.SaveChangesAsync(cancellationToken);
    }
}

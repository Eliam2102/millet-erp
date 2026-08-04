using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// GI-PR4 (doc 12 §D4/Q3): proyección del depósito esperado por la
// diferencia negativa de una liquidación de viáticos. Se conciliará
// contra el movimiento bancario del depósito del empleado igual que las
// expectativas de Caja (RN-6). Dedupe por solicitud (la liberación es
// terminal) además de la marca de idempotencia por evento.
// ============================================================================

public sealed record ProyectarDepositoViaticosCommand(
    Guid EventoId,
    DepositoViaticosEsperadoPayload Payload) : IRequest;

public sealed class ProyectarDepositoViaticosHandler : IRequestHandler<ProyectarDepositoViaticosCommand>
{
    public const string EventType = DepositoViaticosEsperadoPayload.EventType;

    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;

    public ProyectarDepositoViaticosHandler(TesoreriaDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task Handle(ProyectarDepositoViaticosCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;

        var existente = await _db.DepositosConfirmacion
            .AsNoTracking()
            .AnyAsync(d => d.SolicitudViaticosId == p.SolicitudViaticosId, cancellationToken);

        if (!existente && p.MontoEsperado > 0 && p.SolicitudViaticosId != Guid.Empty)
        {
            _db.DepositosConfirmacion.Add(DepositoConfirmacion.CrearExpectativaViaticos(
                empresaId: p.EmpresaId,
                solicitudViaticosId: p.SolicitudViaticosId,
                montoEsperado: p.MontoEsperado,
                moneda: p.Moneda));
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, EventType, ahora,
            detalle: $"solicitud={p.SolicitudViaticosId} esperado={p.MontoEsperado} {p.Moneda}"));

        await _db.SaveChangesAsync(cancellationToken);
    }
}

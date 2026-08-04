using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// TES-PR7 (§3.3 paso 5): sesión de caja cerrada con efectivo declarado X →
// expectativa de que aparezca un depósito ~X en banco. Cierra
// PLATFORM-TODO(<TesoreriaCajaSesion>). Idempotente por CajaSesionId
// (índice único de respaldo). Sesiones sin efectivo declarado no generan
// expectativa (no hay nada que depositar) pero sí marcan el evento como
// procesado.
// ============================================================================

public sealed record ProyectarExpectativaCajaCommand(
    Guid EventoId,
    CajaSesionCerradaPayload Payload) : IRequest;

public sealed class ProyectarExpectativaCajaHandler : IRequestHandler<ProyectarExpectativaCajaCommand>
{
    public const string EventType = CajaSesionCerradaPayload.EventType;

    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;

    public ProyectarExpectativaCajaHandler(TesoreriaDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task Handle(ProyectarExpectativaCajaCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;

        if (p.EfectivoDeclarado > 0)
        {
            var existente = await _db.DepositosConfirmacion
                .AsNoTracking()
                .AnyAsync(d => d.CajaSesionId == p.CajaSesionId, cancellationToken);

            if (!existente)
            {
                _db.DepositosConfirmacion.Add(DepositoConfirmacion.CrearExpectativaCaja(
                    empresaId: p.EmpresaId,
                    cajaSesionId: p.CajaSesionId,
                    efectivoDeclarado: p.EfectivoDeclarado,
                    diaOperacion: p.DiaOperacion));
            }
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, EventType, ahora,
            detalle: $"cajaSesion={p.CajaSesionId} efectivo={p.EfectivoDeclarado} dia={p.DiaOperacion:yyyy-MM-dd}"));

        await _db.SaveChangesAsync(cancellationToken);
    }
}

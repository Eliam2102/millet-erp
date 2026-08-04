using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.EventListeners;

// ============================================================================
// TES-PR7 (§3.3, TES-9): cada propuesta de aplicación de CxC se proyecta a
// la bandeja de depósitos por confirmar como `DepositoConfirmacion`
// Pendiente. La escribe SOLO este listener (y la resolución local de
// confirmar/rechazar) — la creación nunca entra por endpoint. Idempotente
// por PropuestaCxcId (índice único de respaldo): una re-entrega no
// duplica la fila.
// ============================================================================

public sealed record ProyectarPropuestaAplicacionCommand(
    Guid EventoId,
    PropuestaAplicacionCreadaPayload Payload) : IRequest;

public sealed class ProyectarPropuestaAplicacionHandler : IRequestHandler<ProyectarPropuestaAplicacionCommand>
{
    public const string EventType = PropuestaAplicacionCreadaPayload.EventType;

    private static readonly JsonSerializerOptions FacturasJsonOpts = new() { WriteIndented = false };

    private readonly TesoreriaDbContext _db;
    private readonly IClock _clock;

    public ProyectarPropuestaAplicacionHandler(TesoreriaDbContext db, IClock clock)
    {
        _db = db; _clock = clock;
    }

    public async Task Handle(ProyectarPropuestaAplicacionCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;
        var ahora = _clock.UtcNow;

        var existente = await _db.DepositosConfirmacion
            .AsNoTracking()
            .AnyAsync(d => d.PropuestaCxcId == p.PropuestaId, cancellationToken);

        if (!existente)
        {
            // Eventos anteriores a la extensión aditiva llegan sin detalle
            // de facturas: la confirmación seguirá siendo posible pero el
            // confirmado.v1 saldrá con Facturas=[] y Facturación no podrá
            // emitir el REPP automático (queda el endpoint manual).
            var facturas = p.Facturas ?? [];

            _db.DepositosConfirmacion.Add(DepositoConfirmacion.CrearDesdePropuesta(
                empresaId: p.EmpresaId,
                propuestaCxcId: p.PropuestaId,
                clienteId: p.ClienteId,
                depositoRef: p.DepositoRef,
                montoDeposito: p.MontoDeposito,
                moneda: p.Moneda,
                facturasJson: JsonSerializer.Serialize(facturas, FacturasJsonOpts)));
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            command.EventoId, EventType, ahora,
            detalle: $"propuesta={p.PropuestaId} deposito={p.DepositoRef} {p.MontoDeposito} {p.Moneda}"));

        await _db.SaveChangesAsync(cancellationToken);
    }
}

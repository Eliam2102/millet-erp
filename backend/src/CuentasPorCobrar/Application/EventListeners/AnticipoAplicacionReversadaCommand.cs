using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Eventos;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.EventListeners;

/// <summary>
/// Listener de <c>facturacion.anticipo.aplicacion-reversada.v1</c> (M-344): la reversa interna autorizada devuelve a la
/// factura el saldo que la NC de amortización le había descontado. Marca como revertido el movimiento de esa NC y
/// registra un movimiento de reversa cuyo origen es el Id de la reversa (el mismo que guarda Facturación en el anticipo),
/// de modo que ambos lados quedan ligados. Idempotente por evento.
/// </summary>
public sealed record AnticipoAplicacionReversadaCommand(Guid EventId, AnticipoAplicacionReversadaPayload Payload) : IRequest;

public sealed class AnticipoAplicacionReversadaHandler(CuentasPorCobrarDbContext db, IClock clock)
    : IRequestHandler<AnticipoAplicacionReversadaCommand>
{
    public const string EventType = "facturacion.anticipo.aplicacion-reversada.v1";

    public async Task Handle(AnticipoAplicacionReversadaCommand request, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AsNoTracking().AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken))
            return;

        var p = request.Payload;
        var factura = await db.FacturasCartera.FirstOrDefaultAsync(f => f.FacturaVentaId == p.FacturaVentaId, cancellationToken)
            ?? throw new EntityNotFoundException("FC_NO_PROYECTADA",
                $"La factura {p.FacturaVentaId} de la reversa {p.ReversaId} no está en cartera todavía — retry.");

        factura.RevertirNotaCredito(p.Importe);
        var original = await db.MovimientosCartera.FirstOrDefaultAsync(m => m.FacturaCarteraId == factura.Id
            && m.Tipo == TipoMovimientoCartera.NotaCredito && m.OrigenComprobanteId == p.NotaCreditoId && !m.Revertido, cancellationToken);
        original?.Revertir(p.OcurridoEn);
        db.MovimientosCartera.Add(new MovimientoCartera(
            p.EmpresaId, factura.Id, TipoMovimientoCartera.ReversaAplicacionAnticipo, p.ReversaId, p.Importe, p.OcurridoEn));

        db.EventosProcesados.Add(new EventoProcesado(request.EventId, EventType, clock.UtcNow,
            $"Reversa={p.ReversaId}: {p.Importe} regresan al saldo de la factura {factura.Folio} (anticipo {p.AnticipoId})"));
        await db.SaveChangesAsync(cancellationToken);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Domain.Eventos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
namespace Millet.Tesoreria.Application.EventListeners;
public sealed record PasivoRetiradoDePagoPayload(Guid EmpresaId, DateTimeOffset OcurridoEn, Guid FacturaProveedorId,
    Guid ProveedorId, string Moneda, string Motivo)
{ public const string EventType = "cuentas_por_pagar.pasivo.retirado-de-pago.v1"; }
public sealed record RetirarPasivoDePagoCommand(Guid EventoId, PasivoRetiradoDePagoPayload Payload) : IRequest;
public sealed class RetirarPasivoDePagoHandler(TesoreriaDbContext db, IClock clock) : IRequestHandler<RetirarPasivoDePagoCommand>
{
    public async Task Handle(RetirarPasivoDePagoCommand c, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AnyAsync(x => x.EventoId == c.EventoId && x.EventoTipo == PasivoRetiradoDePagoPayload.EventType, cancellationToken)) return;
        var p = c.Payload;
        var pasivo = await db.PasivosPendientesPago.FirstOrDefaultAsync(x => x.FacturaProveedorId == p.FacturaProveedorId, cancellationToken);
        if (pasivo is null)
        {
            // Conserva el retiro aunque llegue antes de la autorización, por reordenamiento de Service Bus.
            pasivo = new PasivoPendientePago(p.EmpresaId, p.FacturaProveedorId, p.ProveedorId, null, 0, 0, p.Moneda,
                null, DateOnly.FromDateTime(p.OcurridoEn.UtcDateTime), null, null, clock.UtcNow);
            db.PasivosPendientesPago.Add(pasivo);
        }
        pasivo.RetirarDePago(p.Motivo, p.OcurridoEn);
        // T9.1-A: el pasivo sale de las corridas vivas (borrador/autorización) con aviso; en las autorizadas solo se avisa.
        if (pasivo.PagoBloqueado)
            foreach (var corrida in await db.CorridasPago.Include(x => x.Lineas).Include(x => x.Firmas)
                         .Where(x => x.Lineas.Any(l => l.Viva && l.FacturaProveedorId == p.FacturaProveedorId)).ToListAsync(cancellationToken))
                corrida.RetirarPasivo(p.FacturaProveedorId, p.Motivo);
        db.EventosProcesados.Add(new EventoProcesado(c.EventoId, PasivoRetiradoDePagoPayload.EventType, clock.UtcNow));
        await db.SaveChangesAsync(cancellationToken);
    }
}

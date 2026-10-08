using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Eventos;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.EventListeners;

/// <summary>Sucursal emisora configurable de cobros bancarios; se resuelve al emitir manualmente.</summary>
public sealed class ReppAutomaticoOptions
{
    public const string SectionName = "Facturacion:ReppAutomatico";
    public string? SucursalClave { get; set; }
}

// Se conserva el nombre del comando interno para los consumidores existentes.
public sealed record EmitirReppDesdePagoConfirmadoCommand(
    Guid EventoId, PagoClienteConfirmadoPayload Payload) : IRequest;

public sealed class EmitirReppDesdePagoConfirmadoHandler(FacturacionDbContext db, IClock clock)
    : IRequestHandler<EmitirReppDesdePagoConfirmadoCommand>
{
    public const string EventType = PagoClienteConfirmadoPayload.EventType;

    public async Task Handle(EmitirReppDesdePagoConfirmadoCommand command, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AnyAsync(e => e.EventoId == command.EventoId && e.EventoTipo == EventType, cancellationToken))
            return;
        var p = command.Payload;
        if (!await db.ReppPendientes.AnyAsync(r => r.MovimientoBancarioId == p.MovimientoBancarioId, cancellationToken))
            db.ReppPendientes.Add(new ReppPendiente(p.EmpresaId, p.ClienteId, p.MovimientoBancarioId,
                p.CuentaBancariaId, p.PropuestaId, p.Monto, p.Moneda, p.FechaValor, p.Referencia,
                p.Facturas?.Select(f => new RelacionRepp(f.FacturaVentaId, f.ImporteAplicado)) ?? []));

        // Pendiente y dedupe se confirman juntos. Nunca se invoca al PAC desde el evento.
        db.EventosProcesados.Add(new EventoProcesado(command.EventoId, EventType, clock.UtcNow,
            $"pendiente REP; movimiento={p.MovimientoBancarioId}"));
        await db.SaveChangesAsync(cancellationToken);
    }
}

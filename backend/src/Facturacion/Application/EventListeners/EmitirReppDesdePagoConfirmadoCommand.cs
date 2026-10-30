using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Eventos;
using Millet.Facturacion.Domain.Ports;
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

public sealed class EmitirReppDesdePagoConfirmadoHandler(FacturacionDbContext db, IClock clock,
    ICuentaOrdenanteMovimientoReadPort? ordenantes = null)
    : IRequestHandler<EmitirReppDesdePagoConfirmadoCommand>
{
    public const string EventType = PagoClienteConfirmadoPayload.EventType;

    public async Task Handle(EmitirReppDesdePagoConfirmadoCommand command, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AnyAsync(e => e.EventoId == command.EventoId && e.EventoTipo == EventType, cancellationToken))
            return;
        var p = command.Payload;
        // C7.4: un pendiente por cobro confirmado (propuesta de CxC), no por movimiento:
        // un depósito identificado en partes (sobrante) genera un REP por cada propuesta.
        if (!await db.ReppPendientes.AnyAsync(r => r.MovimientoBancarioId == p.MovimientoBancarioId
                && r.PropuestaId == p.PropuestaId, cancellationToken))
        {
            // 86e3tfdve (opción A): la ordenante no viaja en el evento (cuidados-infra §4 de
            // Tesorería); se lee del movimiento por puerto y queda en el pendiente.
            var ordenante = ordenantes is null ? null : await ordenantes.ObtenerAsync(p.MovimientoBancarioId, cancellationToken);
            db.ReppPendientes.Add(new ReppPendiente(p.EmpresaId, p.ClienteId, p.MovimientoBancarioId,
                p.CuentaBancariaId, p.PropuestaId, p.Monto - p.SaldoAFavorPorIdentificar, p.Moneda, p.FechaValor, p.Referencia,
                p.Facturas?.Select(f => new RelacionRepp(f.FacturaVentaId, f.ImporteAplicado)) ?? [], ordenante));
        }

        // Pendiente y dedupe se confirman juntos. Nunca se invoca al PAC desde el evento.
        db.EventosProcesados.Add(new EventoProcesado(command.EventoId, EventType, clock.UtcNow,
            $"pendiente REP; movimiento={p.MovimientoBancarioId} propuesta={p.PropuestaId}"));
        await db.SaveChangesAsync(cancellationToken);
    }
}

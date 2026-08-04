using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Ports.Tesoreria;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.Eventos.Cxp;

/// <summary>
/// Command que <see cref="Infrastructure.Workers.CxpEventListenerWorker"/>
/// despacha cuando recibe <c>cuentas_por_pagar.factura.pago-aplicado.v1</c>.
/// Cierra el PLATFORM-TODO <c>&lt;TesoreriaEventListenerCompras&gt;</c> sin
/// listener de Tesorería ni proyección local de facturas: CxP (dueño de
/// <c>importe_pagado</c>) publica el acumulado pagado por OC ya calculado
/// y Compras solo lo aplica.
///
/// <para>
/// Dedupea contra <c>compras.eventos_procesados</c> y publica el evento
/// in-proc <see cref="PagoFacturaProveedorEvent"/> que
/// <c>PagoFacturaProveedorListener</c> (F5-PR2, hasta hoy huérfano)
/// consume invocando <c>OrdenCompra.RegistrarPago</c> — con cierre
/// automático de la OC cuando las 3 dimensiones quedan completas y
/// reapertura si una reversa reduce el acumulado.
/// </para>
/// </summary>
public sealed record FacturaPagoAplicadoCommand(
    Guid EventoId,
    FacturaPagoAplicadoPayload Payload) : IRequest;

public sealed class FacturaPagoAplicadoCommandHandler
    : IRequestHandler<FacturaPagoAplicadoCommand>
{
    public const string EventType = "cuentas_por_pagar.factura.pago-aplicado.v1";

    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<FacturaPagoAplicadoCommandHandler> _logger;

    public FacturaPagoAplicadoCommandHandler(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<FacturaPagoAplicadoCommandHandler> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(FacturaPagoAplicadoCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;

        var notification = new PagoFacturaProveedorEvent(
            OrdenCompraId: p.OrdenCompraId,
            EmpresaId: p.EmpresaId,
            MontoPagadoAcumulado: p.MontoPagadoAcumuladoOc,
            OcurridoEn: p.OcurridoEn);
        await _publisher.Publish(notification, cancellationToken);

        await MarcarProcesado(
            command.EventoId,
            $"FacturaId={p.FacturaProveedorId} OcId={p.OrdenCompraId} AcumuladoOc={p.MontoPagadoAcumuladoOc}",
            cancellationToken);

        // El listener `PagoFacturaProveedorListener` ya hizo SaveChanges
        // sobre la OC; aquí solo persistimos la marca de idempotencia.
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Pago de factura {FacturaId} proyectado a OC {OcId}. AcumuladoOc={Acumulado}",
            p.FacturaProveedorId, p.OrdenCompraId, p.MontoPagadoAcumuladoOc);
    }

    private async Task MarcarProcesado(Guid eventoId, string? observaciones, CancellationToken cancellationToken)
    {
        var yaExiste = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == eventoId && e.EventoTipo == EventType, cancellationToken);
        if (yaExiste) return;

        _db.EventosProcesados.Add(new EventoProcesado(eventoId, EventType, observaciones));
    }
}

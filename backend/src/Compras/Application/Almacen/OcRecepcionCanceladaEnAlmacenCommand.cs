using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Almacen;

/// <summary>
/// Command que <see cref="Infrastructure.Workers.AlmacenEventListenerWorker"/> despacha al recibir
/// <c>almacen.recepcion_oc.cancelada.v1</c> (86e3tabdp): lo recibido de cada línea de OC baja en la cantidad de la
/// recepción cancelada (<c>max(0, recibido − cantidad en unidad de la OC)</c>) y vuelve a quedar pendiente. Reutiliza el
/// evento in-proc de devolución, que aplica <c>OrdenCompra.RegistrarRecepcionLinea</c> (con reapertura si la OC estaba
/// Cerrada). A diferencia de la devolución 8.B, no omite líneas por quedar bajo lo facturado: cancelar una recepción mal
/// capturada es válido aunque ya haya factura (CxP la desliga) y el check de BD lo permite.
/// </summary>
public sealed record OcRecepcionCanceladaEnAlmacenCommand(
    Guid EventoId,
    OcRecepcionCanceladaAlmacenPayload Payload) : IRequest;

public sealed class OcRecepcionCanceladaEnAlmacenHandler
    : IRequestHandler<OcRecepcionCanceladaEnAlmacenCommand>
{
    public const string EventType = "almacen.recepcion_oc.cancelada.v1";

    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<OcRecepcionCanceladaEnAlmacenHandler> _logger;

    public OcRecepcionCanceladaEnAlmacenHandler(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<OcRecepcionCanceladaEnAlmacenHandler> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(OcRecepcionCanceladaEnAlmacenCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventoId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug("[OcRecepcionCanceladaEnAlmacenHandler] Evento {EventId} ya procesado — skip.", request.EventoId);
            return;
        }

        var p = request.Payload;
        var lineasOc = await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Id == p.OrdenCompraId)
            .SelectMany(o => o.Lineas.Select(l => new { l.Id, l.CantidadRecibida }))
            .ToListAsync(cancellationToken);
        if (lineasOc.Count == 0)
            throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró la OC '{p.OrdenCompraId}' al cancelar la recepción.");

        // Varias líneas del evento pueden caer en la misma línea de OC (principal + material en revisión): se descuentan en secuencia.
        var saldo = lineasOc.ToDictionary(l => l.Id, l => l.CantidadRecibida);
        var eventosDescuento = new List<OcDevolucionRegistradaEvent>();
        foreach (var linea in p.Lineas.Where(l => l.LineaOcId is not null))
        {
            var lineaOcId = linea.LineaOcId!.Value;
            // Las excepciones se lanzan ANTES de marcar el evento: el worker reintenta / dead-letter sin descontar dos veces.
            if (!saldo.TryGetValue(lineaOcId, out var actual))
                throw new BusinessRuleException("RECEPCION_CANCELADA_LINEA_OC_NO_RESOLUBLE",
                    $"La línea de OC {lineaOcId} de la recepción {p.RecepcionId} no existe en la OC {p.OrdenCompraId}.");
            saldo[lineaOcId] = Math.Max(0m, actual - linea.CantidadDocumento);
            eventosDescuento.Add(new OcDevolucionRegistradaEvent(
                OrdenCompraId: p.OrdenCompraId,
                LineaOrdenCompraId: lineaOcId,
                EmpresaId: p.EmpresaId,
                CantidadAcumuladaAjustada: saldo[lineaOcId],
                OcurridoEn: p.OcurridoEn));
        }

        foreach (var evento in eventosDescuento)
            await _publisher.Publish(evento, cancellationToken);

        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventoId,
            EventType,
            $"Recepcion={p.RecepcionId} OC={p.OrdenCompraId} Lineas={p.Lineas.Count}"));

        await _db.SaveChangesAsync(cancellationToken);
    }
}

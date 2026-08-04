using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Almacen;

/// <summary>
/// Command despachado por <c>AlmacenEventListenerWorker</c> al recibir
/// <c>almacen.oc_recepcion.registrada.v1</c> del topic
/// <c>almacen-events</c>. Para cada línea del payload con
/// <c>LineaOcId</c> no-null, calcula el acumulado nuevo
/// (<c>CantidadRecibida_actual + Cantidad_evento</c>) e invoca
/// <c>OrdenCompra.RegistrarRecepcionLinea</c>.
///
/// <para>
/// <b>Idempotencia</b>: dedupe contra <c>compras.eventos_procesados</c>
/// por <c>(EventoId, EventoTipo)</c>. El worker ya verifica antes de
/// despachar; el handler también inserta la marca en la misma TX por
/// defensa en profundidad.
/// </para>
///
/// <para>
/// <b>Sub-estado</b>: tras cada línea, <c>RegistrarRecepcionLinea</c>
/// retorna un <c>RecalcularSubEstadosResultado</c> con
/// <see cref="OrdenCompraCerradaEvent"/> si la OC se cerró
/// automáticamente al completar las 3 dimensiones (Recepción + Facturación
/// + Pago). Publicamos el evento out-of-the-box vía MediatR para que los
/// suscriptores existentes (Almacén / Requisiciones) reaccionen.
/// </para>
///
/// <para>
/// <b>Líneas variante B</b>: payloads con <c>LineaOcId = null</c>
/// (materiales directos antes de conciliación) se loggean y omiten —
/// no es posible actualizar sub-estado sin línea destino. Cuando llegue
/// la factura, el handler de conciliación cerrará la brecha.
/// </para>
/// </summary>
public sealed record OcRecepcionEnAlmacenCommand(
    Guid EventoId,
    OcRecepcionRegistradaAlmacenPayload Payload) : IRequest;

public sealed class OcRecepcionEnAlmacenHandler
    : IRequestHandler<OcRecepcionEnAlmacenCommand>
{
    public const string EventType = "almacen.oc_recepcion.registrada.v1";

    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<OcRecepcionEnAlmacenHandler> _logger;

    public OcRecepcionEnAlmacenHandler(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<OcRecepcionEnAlmacenHandler> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(OcRecepcionEnAlmacenCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventoId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug(
                "[OcRecepcionEnAlmacenHandler] Evento {EventId} ya procesado — skip.",
                request.EventoId);
            return;
        }

        var p = request.Payload;

        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == p.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{p.OrdenCompraId}' al proyectar recepción {p.RecepcionId}.");

        var eventosCierre = new List<OrdenCompraCerradaEvent>();
        // Eventos in-proc para que `OcRecepcionRegistradaListener` actualice
        // la línea de RQ correspondiente (cierra el ciclo RQ → OC → recep →
        // RQ.CantidadRecibida). Sólo aplica a líneas que vienen de una RQ;
        // las manuales (RequisicionId == null) no propagan.
        var eventosRq = new List<OcRecepcionRegistradaEvent>();

        foreach (var linea in p.Lineas)
        {
            if (linea.LineaOcId is not { } lineaOcId)
            {
                _logger.LogInformation(
                    "[OcRecepcionEnAlmacenHandler] Línea sin LineaOcId (variante B pre-conciliación). Recepcion={RecepcionId} LineaRecepcion={LineaRecepcionId}",
                    p.RecepcionId, linea.LineaRecepcionId);
                continue;
            }

            var lineaOc = oc.Lineas.FirstOrDefault(l => l.Id == lineaOcId);
            if (lineaOc is null)
            {
                _logger.LogWarning(
                    "[OcRecepcionEnAlmacenHandler] LineaOcId {LineaOcId} no existe en OC {OcId}. Skip línea pero acepto el evento.",
                    lineaOcId, p.OrdenCompraId);
                continue;
            }

            var acumulado = lineaOc.CantidadRecibida + linea.Cantidad;
            var resultado = oc.RegistrarRecepcionLinea(lineaOcId, acumulado, p.OcurridoEn);

            if (resultado.Cerrada is { } cerrada)
            {
                eventosCierre.Add(cerrada);
            }

            // Si la línea de OC vino de una RQ, encolar evento in-proc.
            // El `CantidadRecibida` aquí es el DELTA de esta recepción
            // (no el acumulado) — `Requisicion.RegistrarRecepcion` lo suma
            // sobre la línea correspondiente.
            if (lineaOc.RequisicionId is { } rqId
                && lineaOc.LineaRequisicionId is { } rqLineaId)
            {
                eventosRq.Add(new OcRecepcionRegistradaEvent(
                    RequisicionId: rqId,
                    LineaRequisicionId: rqLineaId,
                    OrdenCompraId: oc.Id,
                    EmpresaId: oc.EmpresaId,
                    CantidadRecibida: linea.Cantidad,
                    OcurridoEn: p.OcurridoEn));
            }
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventoId,
            EventType,
            $"Recepcion={p.RecepcionId} OC={p.OrdenCompraId} Lineas={p.Lineas.Count}"));

        await _db.SaveChangesAsync(cancellationToken);

        // OrdenCompraCerradaEvent puede emitirse varias veces si múltiples
        // líneas cierran su contador en el mismo evento; el agregado
        // garantiza que solo la primera dispara la transición (idempotente
        // en el dominio), pero publicamos todas para que los suscriptores
        // decidan por su cuenta.
        foreach (var cerrada in eventosCierre)
        {
            await _publisher.Publish(cerrada, cancellationToken);
        }

        // Actualización RQ best-effort. Si la RQ no está en EnSurtido
        // (rare — debería estarlo si la OC tomó líneas de la RQ), el
        // dominio lanza BusinessRuleException; loggeamos + continuamos para
        // no abandonar el procesamiento del evento de Almacén (el sub-estado
        // de OC ya quedó persistido y es la fuente autoritativa).
        foreach (var evRq in eventosRq)
        {
            try
            {
                await _publisher.Publish(evRq, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[OcRecepcionEnAlmacenHandler] Error actualizando RQ {RqId} (línea {LineaId}) tras recepción OC {OcId}. OC sí se actualizó.",
                    evRq.RequisicionId, evRq.LineaRequisicionId, evRq.OrdenCompraId);
            }
        }

        _logger.LogInformation(
            "[OcRecepcionEnAlmacenHandler] Recepción {RecepcionId} aplicada a OC {OcId}. Líneas={Lineas} Cerrada={Cerrada}",
            p.RecepcionId, p.OrdenCompraId, p.Lineas.Count, eventosCierre.Count > 0);
    }
}

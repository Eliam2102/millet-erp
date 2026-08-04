using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Events;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Almacen;

/// <summary>
/// Command despachado por <c>AlmacenEventListenerWorker</c> al recibir
/// <c>almacen.salida_requisicion.registrada.v1</c> del topic
/// <c>almacen-events</c> (ADR-0043, canal de entrega Almacén→Compras).
/// Por cada línea con <c>LineaRqId</c> no-null acumula
/// <c>CantidadEntregada</c> en la línea de RQ vía
/// <c>Requisicion.RegistrarEntrega</c>.
///
/// <para>
/// <b>De un salto</b> (a diferencia de la recepción, que es de dos saltos por
/// el agregado OC intermedio): aquí no hay agregado intermedio, así que el
/// dedupe (<see cref="EventoProcesado"/>) y la mutación de la RQ ocurren en la
/// <b>misma transacción</b> — idempotencia fuerte sin doble suma.
/// </para>
///
/// <para>
/// <b>Cierre dormido</b>: <c>RegistrarEntrega</c> solo transiciona a Cerrada
/// si la RQ está en <c>EnSurtido</c> y todo se entregó. En el PR #1 el cierre
/// viejo gana antes, así que aquí casi siempre se acumula sobre una RQ ya
/// terminal (lo cual <c>RegistrarEntrega</c> tolera).
/// </para>
///
/// <para>
/// <b>Vale</b>: payload con <c>RqId == null</c> (salida por vale, sin RQ) →
/// solo se marca el evento como procesado, no hay entrega que proyectar.
/// </para>
/// </summary>
public sealed record SalidaRequisicionEnAlmacenCommand(
    Guid EventoId,
    SalidaRequisicionRegistradaAlmacenPayload Payload) : IRequest;

public sealed class SalidaRequisicionEnAlmacenHandler
    : IRequestHandler<SalidaRequisicionEnAlmacenCommand>
{
    public const string EventType = "almacen.salida_requisicion.registrada.v1";

    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<SalidaRequisicionEnAlmacenHandler> _logger;

    public SalidaRequisicionEnAlmacenHandler(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<SalidaRequisicionEnAlmacenHandler> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(SalidaRequisicionEnAlmacenCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventoId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug(
                "[SalidaRequisicionEnAlmacenHandler] Evento {EventId} ya procesado — skip.",
                request.EventoId);
            return;
        }

        var p = request.Payload;

        // Vale (sin RQ): no hay entrega que proyectar. Marca procesado y termina.
        if (p.RqId is not { } rqId)
        {
            _db.EventosProcesados.Add(new EventoProcesado(
                request.EventoId, EventType, $"Salida={p.SalidaId} sin RQ (vale) — sin entrega."));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        var rq = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == rqId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró RQ '{rqId}' al proyectar la entrega de la salida {p.SalidaId}.");

        var eventosCierre = new List<RequisicionCerradaEvent>();

        foreach (var linea in p.Lineas)
        {
            if (linea.LineaRqId is not { } lineaRqId)
            {
                // Línea sin RQ (no esperable en variante A, pero robusto).
                continue;
            }

            if (!rq.Lineas.Any(l => l.Id == lineaRqId))
            {
                _logger.LogWarning(
                    "[SalidaRequisicionEnAlmacenHandler] LineaRqId {LineaRqId} no existe en RQ {RqId}. Skip línea pero acepto el evento. Salida={SalidaId}",
                    lineaRqId, rqId, p.SalidaId);
                continue;
            }

            var resultado = rq.RegistrarEntrega(lineaRqId, linea.Cantidad, p.OcurridoEn);

            if (resultado.ExcedioTecho)
            {
                // Advertencia estructurada (ADR-0043): la entrega superó el
                // techo físico disponible. NO es error — el canal en el PR #1
                // está dormido y recibe datos de convivencia imperfectos
                // (salidas pure-stock sobre RQs ya Cerrada por el cierre viejo).
                _logger.LogWarning(
                    "[SalidaRequisicionEnAlmacenHandler] Entrega excede el techo físico. RqId={RqId} LineaRqId={LineaRqId} TotalEntregado={Total} Techo={Techo} Salida={SalidaId}",
                    rqId, lineaRqId, resultado.TotalEntregado, resultado.TechoDisponible, p.SalidaId);
            }

            if (resultado.CierreEvento is { } cierre)
            {
                eventosCierre.Add(cierre);
            }
        }

        // Publicar el cierre ANTES del SaveChanges para que el integration
        // mapper de Cerrada inserte la fila a outbox en la MISMA TX (mismo
        // patrón que RegistrarRecepcionHandler). En el PR #1 esto queda
        // dormido (el cierre viejo cierra antes).
        foreach (var cierre in eventosCierre)
        {
            await _publisher.Publish(cierre, cancellationToken);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventoId, EventType, $"Salida={p.SalidaId} RQ={rqId} Lineas={p.Lineas.Count}"));

        // Dedupe + acumulación de la RQ (+ outbox del cierre si aplica) en
        // una sola transacción → idempotencia fuerte.
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[SalidaRequisicionEnAlmacenHandler] Entrega de salida {SalidaId} aplicada a RQ {RqId}. Lineas={Lineas} Cerrada={Cerrada}",
            p.SalidaId, rqId, p.Lineas.Count, eventosCierre.Count > 0);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Almacen;

/// <summary>
/// Command que <see cref="Infrastructure.Workers.AlmacenEventListenerWorker"/>
/// despacha cuando recibe <c>almacen.oc_devolucion.registrada.v1</c>
/// (GAP-5 de la verificación e2e 2026-07-15 — hasta hoy el evento se
/// trataba como informativo y el decremento de <c>CantidadRecibida</c>
/// que la tabla canónica de la triada asigna a Compras no ocurría).
///
/// <para>
/// Por cada línea con <c>LineaOcId</c> calcula el acumulado ajustado
/// (<c>CantidadRecibida actual − cantidad devuelta</c>) y publica el
/// evento in-proc <see cref="OcDevolucionRegistradaEvent"/> que
/// <c>OcDevolucionRegistradaListener</c> (F5-PR2, hasta hoy huérfano)
/// consume invocando <c>OrdenCompra.RegistrarRecepcionLinea</c> (set) —
/// con reapertura automática si la OC estaba Cerrada.
/// </para>
/// </summary>
public sealed record OcDevolucionEnAlmacenCommand(
    Guid EventoId,
    OcDevolucionRegistradaAlmacenPayload Payload) : IRequest;

public sealed class OcDevolucionEnAlmacenHandler
    : IRequestHandler<OcDevolucionEnAlmacenCommand>
{
    public const string EventType = "almacen.oc_devolucion.registrada.v1";

    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<OcDevolucionEnAlmacenHandler> _logger;

    public OcDevolucionEnAlmacenHandler(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<OcDevolucionEnAlmacenHandler> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(OcDevolucionEnAlmacenCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventoId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug(
                "[OcDevolucionEnAlmacenHandler] Evento {EventId} ya procesado — skip.",
                request.EventoId);
            return;
        }

        var p = request.Payload;

        if (p.OrdenCompraOrigenId is Guid ordenCompraId)
        {
            // Proyección de acumulados actuales por línea (scalars; el
            // listener in-proc carga el agregado tracked por su cuenta).
            var acumuladosActuales = await _db.OrdenesCompra
                .AsNoTracking()
                .Where(o => o.Id == ordenCompraId)
                .SelectMany(o => o.Lineas.Select(l => new { l.Id, l.CantidadRecibida }))
                .ToDictionaryAsync(l => l.Id, l => l.CantidadRecibida, cancellationToken);

            foreach (var linea in p.Lineas)
            {
                if (linea.LineaOcId is not { } lineaOcId)
                {
                    _logger.LogInformation(
                        "[OcDevolucionEnAlmacenHandler] Línea sin LineaOcId — sin efecto en Compras. Devolucion={DevolucionId} LineaDevolucion={LineaDevolucionId}",
                        p.DevolucionId, linea.LineaDevolucionId);
                    continue;
                }

                if (!acumuladosActuales.TryGetValue(lineaOcId, out var recibidaActual))
                {
                    _logger.LogWarning(
                        "[OcDevolucionEnAlmacenHandler] LineaOcId {LineaOcId} no existe en OC {OcId}. Skip línea pero acepto el evento.",
                        lineaOcId, ordenCompraId);
                    continue;
                }

                await _publisher.Publish(new OcDevolucionRegistradaEvent(
                    OrdenCompraId: ordenCompraId,
                    LineaOrdenCompraId: lineaOcId,
                    EmpresaId: p.EmpresaId,
                    CantidadAcumuladaAjustada: recibidaActual - linea.Cantidad,
                    OcurridoEn: p.OcurridoEn), cancellationToken);
            }
        }
        else
        {
            _logger.LogInformation(
                "[OcDevolucionEnAlmacenHandler] Devolución {DevolucionId} sin OC de origen — sin efecto en Compras.",
                p.DevolucionId);
        }

        _db.EventosProcesados.Add(new EventoProcesado(
            request.EventoId,
            EventType,
            $"Devolucion={p.DevolucionId} OC={p.OrdenCompraOrigenId} Lineas={p.Lineas.Count}"));

        await _db.SaveChangesAsync(cancellationToken);
    }
}

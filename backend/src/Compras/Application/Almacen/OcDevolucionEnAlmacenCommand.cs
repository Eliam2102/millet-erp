using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Almacen;

/// <summary>
/// Command que <see cref="Infrastructure.Workers.AlmacenEventListenerWorker"/>
/// despacha cuando recibe <c>almacen.oc_devolucion.registrada.v1</c>
/// (GAP-5 de la verificación e2e 2026-07-15 — hasta hoy el evento se
/// trataba como informativo y el decremento de <c>CantidadRecibida</c>
/// que la tabla canónica de la triada asigna a Compras no ocurría).
///
/// <para>
/// Por cada línea resuelve la línea de OC (<c>LineaOcId</c> o, si es null,
/// artículo único no-servicio en la OC), calcula el acumulado ajustado
/// (<c>CantidadRecibida actual − cantidad devuelta en unidad de la OC</c>) y publica el
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
            // Proyección de líneas actuales (scalars; el listener in-proc
            // carga el agregado tracked por su cuenta).
            var lineasOc = await _db.OrdenesCompra
                .AsNoTracking()
                .Where(o => o.Id == ordenCompraId)
                .SelectMany(o => o.Lineas.Select(l => new { l.Id, l.ArticuloId, l.EsServicio, l.Cantidad, l.CantidadRecibida, l.CantidadFacturada }))
                .ToListAsync(cancellationToken);

            if (lineasOc.Count == 0)
            {
                throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", $"No se encontró la OC '{ordenCompraId}' al registrar la devolución.");
            }

            // Saldo corriente por línea de OC: varias devoluciones sobre la misma línea
            // se descuentan en secuencia (cada evento lleva el acumulado post-descuento anterior).
            var saldo = lineasOc.ToDictionary(l => l.Id, l => l.CantidadRecibida);
            var facturada = lineasOc.ToDictionary(l => l.Id, l => l.CantidadFacturada);
            var pedida = lineasOc.ToDictionary(l => l.Id, l => l.Cantidad);

            // Resolver todas las líneas antes de publicar: el listener hace SaveChanges
            // por evento, así que una excepción a mitad del bucle dejaría líneas aplicadas
            // y, al reintentar el evento, se descontarían dos veces.
            var eventosDescuento = new List<OcDevolucionRegistradaEvent>();
            foreach (var linea in p.Lineas)
            {
                // Las excepciones de este bucle se lanzan ANTES de agregar EventoProcesado
                // (más abajo): el evento queda sin marcar y el worker reintenta / dead-letter.
                Guid lineaOcId;
                if (linea.LineaOcId is { } idDirecto)
                {
                    lineaOcId = idDirecto;
                }
                else
                {
                    var candidatas = lineasOc
                        .Where(l => !l.EsServicio && l.ArticuloId == linea.ArticuloId)
                        .ToList();
                    if (candidatas.Count != 1)
                    {
                        throw new BusinessRuleException(
                            "DEVOLUCION_LINEA_OC_NO_RESOLUBLE",
                            $"La devolución {p.DevolucionId} trae una línea sin línea de OC y el artículo {linea.ArticuloId} no es único en la OC {ordenCompraId}; corrige y reprocesa el evento.");
                    }

                    lineaOcId = candidatas[0].Id;
                    _logger.LogWarning(
                        "[OcDevolucionEnAlmacenHandler] Línea sin LineaOcId resuelta por artículo. Devolucion={DevolucionId} LineaOc={LineaOcId}",
                        p.DevolucionId, lineaOcId);
                }

                if (!saldo.TryGetValue(lineaOcId, out var saldoActual))
                {
                    throw new BusinessRuleException(
                        "DEVOLUCION_LINEA_OC_NO_RESOLUBLE",
                        $"La línea de OC {lineaOcId} de la devolución {p.DevolucionId} no existe en la OC {ordenCompraId}.");
                }

                // CantidadDocumento = unidad de la OC (M-170); eventos viejos caen a Cantidad.
                var nuevoSaldo = saldoActual - (linea.CantidadDocumento ?? linea.Cantidad);
                if (facturada[lineaOcId] > Math.Max(pedida[lineaOcId], nuevoSaldo))
                {
                    // Check de BD ck_oc_lineas_facturada_max: facturada <= MAX(cantidad, recibida). Almacén ya lo bloquea; si llega
                    // (evento viejo / carrera) se omite la línea y se marca procesado para no entrar en dead-letter.
                    _logger.LogError(
                        "[OcDevolucionEnAlmacenHandler] Devolución {DevolucionId} omitida en línea {LineaOcId}: dejaría recibido ({Saldo}) bajo lo facturado ({Facturada}).",
                        p.DevolucionId, lineaOcId, nuevoSaldo, facturada[lineaOcId]);
                    continue;
                }
                saldo[lineaOcId] = nuevoSaldo;

                eventosDescuento.Add(new OcDevolucionRegistradaEvent(
                    OrdenCompraId: ordenCompraId,
                    LineaOrdenCompraId: lineaOcId,
                    EmpresaId: p.EmpresaId,
                    CantidadAcumuladaAjustada: saldo[lineaOcId],
                    OcurridoEn: p.OcurridoEn));
            }

            foreach (var evento in eventosDescuento)
            {
                await _publisher.Publish(evento, cancellationToken);
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

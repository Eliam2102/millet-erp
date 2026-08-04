using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Idempotencia;
using Millet.Compras.Domain.Ports.Cxp;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.Eventos.Cxp;

/// <summary>
/// Command que <see cref="Infrastructure.Workers.CxpEventListenerWorker"/>
/// despacha cuando recibe <c>cuentas_por_pagar.factura.registrada.v1</c>.
/// Dedupea contra <c>compras.eventos_procesados</c>, fan-out por línea
/// de OC publicando <see cref="FacturaProveedorRegistradaEvent"/> que el
/// listener <c>FacturaProveedorRegistradaListener</c> (in-proc) consume
/// invocando <c>OrdenCompra.RegistrarFacturacionLinea</c>.
/// </summary>
public sealed record FacturaProveedorRegistradaCommand(
    Guid EventoId,
    FacturaProveedorRegistradaPayload Payload) : IRequest;

public sealed class FacturaProveedorRegistradaCommandHandler
    : IRequestHandler<FacturaProveedorRegistradaCommand>
{
    public const string EventType = "cuentas_por_pagar.factura.registrada.v1";

    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<FacturaProveedorRegistradaCommandHandler> _logger;

    public FacturaProveedorRegistradaCommandHandler(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<FacturaProveedorRegistradaCommandHandler> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(FacturaProveedorRegistradaCommand command, CancellationToken cancellationToken)
    {
        var p = command.Payload;

        // Si la factura no tiene líneas con LineaOcId, no hay nada que
        // actualizar (caso operativo: factura sin granularidad por línea
        // — Compras solo afecta sub-estado vía líneas). Persistimos la
        // marca de idempotencia y salimos.
        if (p.LineasAcumuladasOc.Count == 0)
        {
            _logger.LogInformation(
                "Factura {FacturaId} sobre OC {OcId} sin líneas con LineaOcId — sin efecto en Compras.",
                p.FacturaProveedorId, p.OrdenCompraId);
            await MarcarProcesado(
                command.EventoId,
                EventType,
                $"Sin líneas con LineaOcId. FacturaId={p.FacturaProveedorId}",
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        foreach (var linea in p.LineasAcumuladasOc)
        {
            var notification = new FacturaProveedorRegistradaEvent(
                OrdenCompraId: p.OrdenCompraId,
                LineaOrdenCompraId: linea.LineaOcId,
                EmpresaId: p.EmpresaId,
                CantidadFacturadaAcumulada: linea.CantidadAcumulada,
                OcurridoEn: p.OcurridoEn);
            await _publisher.Publish(notification, cancellationToken);
        }

        await MarcarProcesado(
            command.EventoId,
            EventType,
            $"FacturaId={p.FacturaProveedorId} OcId={p.OrdenCompraId} Líneas={p.LineasAcumuladasOc.Count}",
            cancellationToken);

        // El listener `FacturaProveedorRegistradaListener` ya hace
        // SaveChanges por cada notification; aquí solo persistimos la
        // marca de idempotencia que insertamos arriba.
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarcarProcesado(Guid eventoId, string eventType, string? observaciones, CancellationToken cancellationToken)
    {
        var yaExiste = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == eventoId && e.EventoTipo == eventType, cancellationToken);
        if (yaExiste) return;

        _db.EventosProcesados.Add(new EventoProcesado(eventoId, eventType, observaciones));
    }
}

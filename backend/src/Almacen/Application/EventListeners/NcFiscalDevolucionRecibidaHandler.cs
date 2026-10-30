using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;

namespace Millet.Almacen.Application.EventListeners;

/// <summary>Espejo del payload de CxP <c>cuentas_por_pagar.nc-fiscal-devolucion.recibida.v1</c>.</summary>
public sealed record NcFiscalDevolucionRecibidaPayload(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid NotaCreditoProveedorId,
    Guid NotaCargoId,
    Guid DevolucionAProveedorId,
    Guid ProveedorId,
    decimal Total,
    string UuidCfdi);

public sealed record NcFiscalDevolucionRecibidaCommand(
    Guid EventId, NcFiscalDevolucionRecibidaPayload Payload) : IRequest;

/// <summary>
/// Concilia la devolución EXACTA (por Id, sin heurística) con la NC fiscal 03 y publica
/// <c>almacen.oc_devolucion.conciliada.v1</c> en la misma unidad de trabajo (SaveChanges + outbox).
/// Si ya estaba conciliada no falla y vuelve a publicar (el acumulado es idempotente en Compras).
/// </summary>
public sealed class NcFiscalDevolucionRecibidaHandler : IRequestHandler<NcFiscalDevolucionRecibidaCommand>
{
    public const string EventType = "cuentas_por_pagar.nc-fiscal-devolucion.recibida.v1";

    private readonly AlmacenDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly IComprasOcReadPort _ocPort;
    private readonly IConversionUnidadPort _conversion;
    private readonly ILogger<NcFiscalDevolucionRecibidaHandler> _logger;

    public NcFiscalDevolucionRecibidaHandler(
        AlmacenDbContext db, IIntegrationEventPublisher events, IComprasOcReadPort ocPort,
        IConversionUnidadPort conversion, ILogger<NcFiscalDevolucionRecibidaHandler> logger)
    {
        _db = db; _events = events; _ocPort = ocPort; _conversion = conversion; _logger = logger;
    }

    public async Task Handle(NcFiscalDevolucionRecibidaCommand request, CancellationToken cancellationToken)
    {
        var p = request.Payload;
        var dev = await _db.Set<DevolucionAProveedor>().FirstOrDefaultAsync(d => d.Id == p.DevolucionAProveedorId, cancellationToken);
        if (dev is null)
        {
            _logger.LogWarning(
                "NC fiscal de devolución {Nc}: devolución {Dev} no existe en Almacén. Evento marcado procesado.",
                p.NotaCreditoProveedorId, p.DevolucionAProveedorId);
            _db.Set<EventoProcesado>().Add(new EventoProcesado(
                request.EventId, EventType, $"Devolución {p.DevolucionAProveedorId} inexistente"));
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (dev.Estado != EstadoDevolucionProveedor.ConciliadaConNcFiscal)
            dev.ConciliarConNcFiscal(p.NotaCreditoProveedorId);

        if (dev.OrdenCompraOrigenId is Guid ocId)
        {
            var oc = await _ocPort.ObtenerAsync(ocId, cancellationToken)
                ?? throw new InvalidOperationException($"OC {ocId} no disponible para acreditar la devolución {dev.Id}.");

            // Base por línea de OC: devoluciones ya conciliadas (en BD) + esta (aún sin guardar, por eso se excluye
            // de la parte "ya conciliadas" y se incluye por Id).
            var baseporLinea = await (
                from d in _db.DevolucionesProveedor.AsNoTracking()
                where d.OrdenCompraOrigenId == ocId
                    && (d.Id == dev.Id || d.Estado == EstadoDevolucionProveedor.ConciliadaConNcFiscal)
                join ld in _db.LineasDevolucionProveedor.AsNoTracking() on d.Id equals ld.DevolucionId
                join lm in _db.LineasMovimiento.AsNoTracking() on ld.LineaRecepcionOrigenId equals (Guid?)lm.Id
                where lm.LineaOcId != null
                group ld.Cantidad by lm.LineaOcId!.Value into g
                select new { LineaOcId = g.Key, Cantidad = g.Sum() }
            ).ToListAsync(cancellationToken);

            var lineas = new List<LineaOcAcreditadaPayload>(baseporLinea.Count);
            foreach (var b in baseporLinea)
            {
                var lineaOc = oc.Lineas.FirstOrDefault(l => l.LineaId == b.LineaOcId)
                    ?? throw new InvalidOperationException($"Línea {b.LineaOcId} no existe en la OC {ocId}.");
                var factor = await RecibidoPorLineaOc.FactorDocumentoABaseAsync(
                    _conversion, lineaOc.ArticuloId, lineaOc.UnidadMedida, cancellationToken);
                lineas.Add(new LineaOcAcreditadaPayload(b.LineaOcId, Math.Round(b.Cantidad / factor, 4)));
            }

            await _events.PublishAsync(new OcDevolucionConciliadaIntegrationEvent(
                dev.EmpresaId, DateTimeOffset.UtcNow, ocId, dev.Id, lineas), cancellationToken);
        }

        _db.Set<EventoProcesado>().Add(new EventoProcesado(
            request.EventId, EventType, $"DevolucionId={dev.Id} NcFiscalId={p.NotaCreditoProveedorId}"));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("DevolucionAProveedor {Dev} conciliada con NC fiscal {Nc}.", dev.Id, p.NotaCreditoProveedorId);
    }
}

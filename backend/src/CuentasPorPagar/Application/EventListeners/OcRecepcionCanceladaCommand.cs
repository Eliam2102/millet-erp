using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.EventListeners;

/// <summary>
/// Listener del integration event <c>almacen.recepcion_oc.cancelada.v1</c> (86e3tabdp). Borra la proyección local de la
/// recepción, desliga las facturas que la habían elegido y republica el pasivo de las facturas Autorizadas de la OC con
/// la elegibilidad ya sin esa recepción. <b>No</b> genera nota de cargo: no es una devolución a proveedor.
/// Idempotente por <see cref="EventoProcesado"/> y porque desligar/borrar repetido no cambia nada.
/// </summary>
public sealed record OcRecepcionCanceladaCommand(
    Guid EventId,
    OcRecepcionCanceladaPayload Payload) : IRequest;

public sealed class OcRecepcionCanceladaHandler : IRequestHandler<OcRecepcionCanceladaCommand>
{
    public const string EventType = "almacen.recepcion_oc.cancelada.v1";

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly Integration.Mappers.PasivoAutorizadoParaPagoMapper _pasivos;
    private readonly ILogger<OcRecepcionCanceladaHandler> _logger;

    public OcRecepcionCanceladaHandler(
        CuentasPorPagarDbContext db,
        IClock clock,
        Integration.Mappers.PasivoAutorizadoParaPagoMapper pasivos,
        ILogger<OcRecepcionCanceladaHandler> logger)
    {
        _db = db; _clock = clock; _pasivos = pasivos; _logger = logger;
    }

    public async Task Handle(OcRecepcionCanceladaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug("[OcRecepcionCanceladaHandler] Evento {EventId} ya procesado — skip.", request.EventId);
            return;
        }

        var p = request.Payload;
        var ahora = _clock.UtcNow;

        var local = await _db.RecepcionesOcLocal.FirstOrDefaultAsync(r => r.RecepcionId == p.RecepcionId, cancellationToken);
        if (local is not null) _db.RecepcionesOcLocal.Remove(local);

        var ligadas = await _db.FacturasProveedor.Where(f => f.RecepcionId == p.RecepcionId).ToListAsync(cancellationToken);
        foreach (var f in ligadas) f.DesligarRecepcion(p.RecepcionId);

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: ahora,
            detalle: $"Recepcion={p.RecepcionId} OC={p.OrdenCompraId} FacturasDesligadas={ligadas.Count}"));

        // Elegibilidad sin la recepción cancelada: lo recibido vigente viene de Almacén (no se resta nada a lo de Compras, que puede
        // haber procesado ya la cancelación o no).
        var vigente = p.Lineas.Where(l => l.LineaOcId != null && l.RecibidoAcumuladoDocumento != null)
            .GroupBy(l => l.LineaOcId!.Value).ToDictionary(g => g.Key, g => g.First().RecibidoAcumuladoDocumento!.Value);
        var autorizadas = await _db.FacturasProveedor.Where(f => f.OrdenCompraId == p.OrdenCompraId &&
            f.Estado == Domain.FacturaProveedor.EstadoPasivo.Autorizada).ToListAsync(cancellationToken);
        foreach (var f in autorizadas)
            await _pasivos.PublicarAsync(new Domain.FacturaProveedor.Events.FacturaProveedorAutorizadaDomainEvent(
                f.EmpresaId, f.Id, f.OrdenCompraId, ahora), cancellationToken, recibidoVigente: vigente);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[OcRecepcionCanceladaHandler] Recepción {RecepcionId} (OC {OcId}) cancelada: {Facturas} factura(s) desligada(s), {Pasivos} pasivo(s) republicado(s).",
            p.RecepcionId, p.OrdenCompraId, ligadas.Count, autorizadas.Count);
    }
}

using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Almacen;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.EventListeners;

/// <summary>
/// Listener del integration event <c>almacen.oc_recepcion.registrada.v1</c>
/// (F5-PR1). Proyecta la recepción al store local
/// <c>recepciones_oc_local</c> para que <c>IAlmacenRecepcionReadPort</c>
/// resuelva sin lookups cross-DbContext.
///
/// <para>
/// **Idempotencia**: dedupe vía <see cref="EventoProcesado"/> por
/// <c>(EventId, EventType)</c>. Re-entregas at-least-once del Service
/// Bus no proyectan dos veces.
/// </para>
/// </summary>
public sealed record OcRecepcionRegistradaCommand(
    Guid EventId,
    OcRecepcionRegistradaPayload Payload) : IRequest;

public sealed class OcRecepcionRegistradaHandler : IRequestHandler<OcRecepcionRegistradaCommand>
{
    public const string EventType = "almacen.oc_recepcion.registrada.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly CuentasPorPagarDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<OcRecepcionRegistradaHandler> _logger;

    public OcRecepcionRegistradaHandler(
        CuentasPorPagarDbContext db,
        IClock clock,
        ILogger<OcRecepcionRegistradaHandler> logger)
    {
        _db = db; _clock = clock; _logger = logger;
    }

    public async Task Handle(OcRecepcionRegistradaCommand request, CancellationToken cancellationToken)
    {
        var dedup = await _db.EventosProcesados
            .AsNoTracking()
            .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken);
        if (dedup)
        {
            _logger.LogDebug(
                "[OcRecepcionRegistradaHandler] Evento {EventId} ya procesado — skip.", request.EventId);
            return;
        }

        var p = request.Payload;

        // Upsert por RecepcionId. Si la recepción ya existe (re-entrega
        // tardía o reconstrucción manual), actualiza factura_pendiente
        // + cfdi_recibido_id que pueden cambiar entre eventos.
        var existente = await _db.RecepcionesOcLocal
            .FirstOrDefaultAsync(r => r.RecepcionId == p.RecepcionId, cancellationToken);

        var lineasJson = JsonSerializer.Serialize(p.Lineas, JsonOptions);
        var ahora = _clock.UtcNow;

        if (existente is null)
        {
            var nueva = new RecepcionOcLocal(
                empresaId: p.EmpresaId,
                recepcionId: p.RecepcionId,
                folioRecepcion: p.FolioRecepcion,
                ordenCompraId: p.OrdenCompraId,
                fechaMovimiento: p.FechaMovimiento,
                facturaPendiente: p.FacturaPendiente,
                cfdiRecibidoId: p.CfdiRecibidoId,
                observaciones: p.Observaciones,
                lineasJson: lineasJson,
                ocurridoEn: p.OcurridoEn,
                proyectadoEn: ahora);
            _db.RecepcionesOcLocal.Add(nueva);
        }
        // Caso UPSERT — solo aplicamos lo que en realidad cambia entre
        // eventos del mismo recepcion_id (raro pero válido).
        // Reescribir aquí cuando exista el método de mutación.

        _db.EventosProcesados.Add(new EventoProcesado(
            eventoId: request.EventId,
            eventoTipo: EventType,
            procesadoEn: ahora,
            detalle: $"Recepcion={p.RecepcionId} OC={p.OrdenCompraId} FacturaPendiente={p.FacturaPendiente}"));

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "[OcRecepcionRegistradaHandler] Recepción {RecepcionId} (OC {OcId}) proyectada localmente. FacturaPendiente={Pendiente}.",
            p.RecepcionId, p.OrdenCompraId, p.FacturaPendiente);
    }
}

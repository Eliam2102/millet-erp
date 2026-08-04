using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.Application.IntegrationEvents;

/// <summary>
/// Evento de integración emitido al Outbox cuando el ERP acepta una
/// cotización del Glass Agent y la persiste como
/// <c>entidad_externa</c> en estado <c>Submitted</c>.
///
/// <para>
/// Consumer esperado: <c>AwDropWorker</c> en PR C — lo recoge del topic
/// <c>integraciones-aw-events</c> via subscription
/// <c>drop-subscription</c> y dispara el HTTP drop al servicio on-prem.
/// </para>
///
/// <para>
/// El <c>EventType</c> sigue la convención del repo:
/// <c>{modulo}.{recurso}.{accion}.v{version}</c>.
/// </para>
/// </summary>
public sealed record AwCotizacionRecibida(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AggregateId,
    string QuoteReference,
    string Sucursal,
    string FilenameSuggestion)
    : IntegrationEvent(EventTypeName, EmpresaId, OcurridoEn)
{
    // EventType conservado en v1 — agregar el campo Sucursal es aditivo
    // (backward-compatible). El único consumer hoy es AwDropWorker, que se
    // actualiza en el mismo PR. Si en el futuro aparece un consumer cross-
    // module que requiera distinguir el shape viejo, ahí bumpear a v2 y
    // actualizar el SqlFilter en infra/modules/servicebus.bicep.
    public const string EventTypeName = "integraciones.aw.cotizacion.recibida.v1";
}

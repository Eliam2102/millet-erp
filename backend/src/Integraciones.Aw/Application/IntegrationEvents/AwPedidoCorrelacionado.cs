using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.Application.IntegrationEvents;

/// <summary>
/// Evento emitido cuando el <c>AwCorrelationWorker</c> encuentra la
/// orden espejo en A+W (<c>pool_auftrag.auftragsnummer_kunde</c> =
/// <c>QuoteReference</c>). La entidad transiciona a <c>Correlated</c>.
///
/// <para>
/// Consumer esperado: <c>SignalR Hub</c> notifica al frontend para que
/// muestre el folio asignado por A+W. También útil para módulos de
/// Facturación / CxC futuros que reaccionen al nacimiento de un pedido.
/// </para>
/// </summary>
public sealed record AwPedidoCorrelacionado(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AggregateId,
    string QuoteReference,
    long AwDocId,
    DateTimeOffset CorrelatedAt)
    : IntegrationEvent(EventTypeName, EmpresaId, OcurridoEn)
{
    public const string EventTypeName = "integraciones.aw.pedido.correlacionado.v1";
}

using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.Application.IntegrationEvents;

/// <summary>
/// Evento emitido cuando el <c>AwDropWorker</c> agota los reintentos
/// del drop al on-prem (timeout, connection refused, 5xx persistente,
/// etc.). La entidad transiciona a <c>FailedDrop</c>.
///
/// <para>
/// Consumer esperado: alertas — el ERP perdió contacto con el
/// servicio on-prem; HCM puede estar caído, drop service muerto, o
/// firewall on-prem cambió. Requiere intervención operacional.
/// </para>
/// </summary>
public sealed record AwEdiEntregaFallida(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AggregateId,
    string QuoteReference,
    string Error,
    string ErrorKind)
    : IntegrationEvent(EventTypeName, EmpresaId, OcurridoEn)
{
    public const string EventTypeName = "integraciones.aw.edi.entrega-fallida.v1";
}

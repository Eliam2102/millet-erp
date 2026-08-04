using Millet.SharedKernel.Application.Integration;

namespace Millet.Integraciones.Aw.Application.IntegrationEvents;

/// <summary>
/// Evento emitido cuando A+W RECHAZÓ el EDI al procesarlo (códigos
/// terminales típicamente <c>(1555)</c>, con detalles posición-por-posición
/// como <c>(1550)</c> artículo inválido y <c>(1603)</c> faltan campos
/// obligatorios). La entidad transiciona a <c>FailedCorrelation</c>.
///
/// <para>
/// El <see cref="AwDocId"/> puede ser <c>null</c> o presente — A+W reserva
/// el ID al inicio del procesamiento aunque la importación falle (per
/// project memory <c>project_aw_doc_id_behavior.md</c>: IDs nunca se reusan).
/// </para>
///
/// <para>
/// Consumer esperado: módulo de alertas / dashboard de operación — el
/// operador ve los códigos y el mensaje en la UI bandeja y decide
/// reintento (corregir EDI) o resolución manual (rechazo definitivo).
/// </para>
/// </summary>
public sealed record AwCotizacionRechazadaPorAw(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid AggregateId,
    string QuoteReference,
    long? AwDocId,
    IReadOnlyList<string> ErrorCodes,
    string ErrorMessage,
    DateTimeOffset FailedAt)
    : IntegrationEvent(EventTypeName, EmpresaId, OcurridoEn)
{
    public const string EventTypeName = "integraciones.aw.cotizacion.rechazada.v1";
}

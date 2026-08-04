namespace Millet.Integraciones.Fiscal.Application.IntegrationEvents;

/// <summary>
/// Evento de integración emitido tras crear o actualizar la
/// <c>ConfiguracionPac</c> de una empresa (PR-4). Informativo —
/// hoy lo consume sólo el logging estructurado para auditoría; cuando
/// el módulo Notificaciones exista, también dispara alertas si la
/// rotación de credenciales no se hace en X días.
///
/// <para>
/// Topic Service Bus: <c>integraciones-fiscal-events</c> (Outbox de
/// <c>IntegracionesFiscalDbContext</c> publicará allá cuando se wire
/// en PR-5/PR-6).
/// </para>
///
/// <para>
/// <b>NUNCA</b> incluye el ApiKey (cifrado o no) ni el hash — solo
/// metadata sin contenido sensible. Los hashes se quedan en BD para
/// detección de rotación interna.
/// </para>
/// </summary>
public sealed record IntegracionesFiscalConfiguracionActualizadaEvent(
    Guid ConfiguracionId,
    Guid EmpresaId,
    short Proveedor,
    string BaseUrl,
    bool Activo,
    bool Rotacion,
    DateTimeOffset OcurridoEn)
    // Sin esta herencia el OutboxIntegrationEventPublisher rechaza el
    // evento con ArgumentException → 500 al guardar la configuración
    // (bug FAC-DET-PR7: nadie había guardado una ConfiguracionPac desde
    // que el publisher valida el tipo).
    : Millet.SharedKernel.Application.Integration.IntegrationEvent(
        EventTypeName, EmpresaId, OcurridoEn)
{
    public const string EventTypeName = "integraciones.fiscal.configuracion-actualizada.v1";
}

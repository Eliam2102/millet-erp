using Millet.SharedKernel.Application.Integration;

namespace Millet.Administracion.Application.Events;

/// <summary>
/// Integration event v1: empresa creada en el catálogo organizacional
/// (F-Admin-PR2.3). EventType: <c>admin.empresa.creada.v1</c>.
///
/// <para>
/// EmpresaId del evento = id de la empresa creada (no la del JWT actor).
/// Permite a consumers de otros módulos (CxC, Contabilidad, BI) inicializar
/// su catálogo local sin polling.
/// </para>
/// <para>
/// PLATFORM-TODO(&lt;AdminOutbox&gt;): el publisher actual encola al
/// <c>IIntegrationEventBuffer</c> scoped pero el
/// <c>CompartidoDbContext</c> no tiene <c>OutboxSaveChangesInterceptor</c>
/// wireado, por lo que el evento se pierde silenciosamente al cerrar el
/// scope. Cuando Administración levante su propio DbContext con tabla
/// outbox propia (o se agregue el interceptor a Compartido), los
/// consumers reales recibirán el evento. Tabla destino esperada:
/// <c>compartido.integration_events_outbox</c>.
/// </para>
/// </summary>
public sealed record EmpresaCreadaEvent(
    Guid EmpresaId,
    string Rfc,
    string RazonSocial,
    DateTimeOffset OcurridoEn)
    : IntegrationEvent("admin.empresa.creada.v1", EmpresaId, OcurridoEn);

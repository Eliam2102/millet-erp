using Millet.SharedKernel.Application.Integration;

namespace Millet.Identidad.Application.Events;

/// <summary>
/// Integration event v1: la matriz de permisos de un rol fue reasignada
/// (F-Admin-PR3.2). EventType: <c>identidad.rol.permisos.actualizados.v1</c>.
///
/// <para>
/// <see cref="PermisoIds"/> es el set completo post-asignación (no un
/// delta) — el batch del comando es atómico. El campo <c>EmpresaId</c>
/// del envelope queda en <see cref="Guid.Empty"/> porque los roles son
/// globales (no por empresa); el consumer debe leer <c>RolId</c> y
/// aplicar lógica propia.
/// </para>
///
/// <para>
/// PLATFORM-TODO(&lt;AdminOutbox&gt;): el publisher actual encola al
/// <c>IIntegrationEventBuffer</c> scoped pero el
/// <c>IdentidadDbContext</c> no tiene <c>OutboxSaveChangesInterceptor</c>
/// wireado, por lo que el evento se pierde silenciosamente al cerrar el
/// scope. Aceptable en MVP — todavía no hay consumers reales.
/// </para>
/// </summary>
public sealed record RolPermisosActualizadosEvent(
    Guid RolId,
    IReadOnlyList<Guid> PermisoIds,
    DateTimeOffset OcurridoEnUtc)
    : IntegrationEvent("identidad.rol.permisos.actualizados.v1", Guid.Empty, OcurridoEnUtc);

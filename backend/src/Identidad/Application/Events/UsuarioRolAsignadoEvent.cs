using Millet.SharedKernel.Application.Integration;

namespace Millet.Identidad.Application.Events;

/// <summary>
/// Integration event v1: se asignó un rol a un usuario dentro de una
/// empresa (F-Admin-PR4.2). EventType:
/// <c>identidad.usuario.rol.asignado.v1</c>.
///
/// <para>
/// El <c>EmpresaId</c> del envelope sí lleva la empresa concreta (la
/// asignación es multi-tenant). El consumer debe leer
/// <see cref="UsuarioId"/> y <see cref="RolId"/> para refrescar el
/// permission cache del usuario en esa empresa.
/// </para>
///
/// <para>
/// PLATFORM-TODO(&lt;AdminOutbox&gt;): el publisher actual encola al
/// <c>IIntegrationEventBuffer</c> scoped pero el
/// <c>IdentidadDbContext</c> no tiene <c>OutboxSaveChangesInterceptor</c>
/// wireado, por lo que el evento se pierde silenciosamente al cerrar el
/// scope. Aceptable en MVP — todavía no hay consumers reales. Mismo
/// patrón que <c>RolPermisosActualizadosEvent</c> (F-Admin-PR3.2).
/// </para>
/// </summary>
public sealed record UsuarioRolAsignadoEvent(
    Guid UsuarioId,
    Guid EmpresaId,
    Guid RolId,
    DateTimeOffset OcurridoEnUtc)
    : IntegrationEvent("identidad.usuario.rol.asignado.v1", EmpresaId, OcurridoEnUtc);

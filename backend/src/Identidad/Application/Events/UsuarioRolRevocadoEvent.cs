using Millet.SharedKernel.Application.Integration;

namespace Millet.Identidad.Application.Events;

/// <summary>
/// Integration event v1: se revocó la asignación de un rol a un usuario
/// en una empresa (F-Admin-PR4.2). EventType:
/// <c>identidad.usuario.rol.revocado.v1</c>.
///
/// <para>
/// PLATFORM-TODO(&lt;AdminOutbox&gt;): mismo gap que
/// <c>UsuarioRolAsignadoEvent</c>: <c>IdentidadDbContext</c> sin
/// <c>OutboxSaveChangesInterceptor</c>, el evento se pierde al cerrar
/// scope. Aceptable en MVP.
/// </para>
/// </summary>
public sealed record UsuarioRolRevocadoEvent(
    Guid UsuarioId,
    Guid EmpresaId,
    Guid RolId,
    DateTimeOffset OcurridoEnUtc)
    : IntegrationEvent("identidad.usuario.rol.revocado.v1", EmpresaId, OcurridoEnUtc);

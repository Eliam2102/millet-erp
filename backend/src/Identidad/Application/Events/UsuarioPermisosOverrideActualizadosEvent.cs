using Millet.SharedKernel.Application.Integration;

namespace Millet.Identidad.Application.Events;

/// <summary>
/// Integration event v1: se reemplazaron (o se restablecieron) las excepciones
/// de permisos de un usuario en una empresa (ADR-0053). EventType:
/// <c>identidad.usuario.permisos-override.actualizados.v1</c>.
///
/// <para>
/// Solo lleva identificadores y conteos post-operación; no lista permisos ni
/// motivos (sin datos sensibles). <see cref="Concedidos"/> y
/// <see cref="Denegados"/> en cero significan "el usuario volvió al rol".
/// </para>
///
/// <para>
/// PLATFORM-TODO(&lt;AdminOutbox&gt;): <c>IdentidadDbContext</c> no tiene el
/// <c>OutboxSaveChangesInterceptor</c> wireado; mismo patrón que
/// <c>UsuarioRolAsignadoEvent</c>.
/// </para>
/// </summary>
public sealed record UsuarioPermisosOverrideActualizadosEvent(
    Guid UsuarioId,
    Guid EmpresaId,
    int Concedidos,
    int Denegados,
    DateTimeOffset OcurridoEnUtc)
    : IntegrationEvent("identidad.usuario.permisos-override.actualizados.v1", EmpresaId, OcurridoEnUtc);

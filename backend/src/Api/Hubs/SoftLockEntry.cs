namespace Millet.Api.Hubs;

/// <summary>
/// Una declaración de presencia de un usuario sobre un recurso
/// (entidad + id) dentro de una empresa. Inmutable; el manager reemplaza
/// la entry completa cuando refresca el heartbeat o cambia de modo.
/// </summary>
/// <param name="Entidad">Nombre del tipo de entidad (e.g. <c>"Requisicion"</c>).</param>
/// <param name="EntidadId">Id de la fila concreta.</param>
/// <param name="EmpresaId">Empresa del usuario — los broadcasts se hacen
/// por grupo <c>empresa:{id}</c> para aislar tenants.</param>
/// <param name="UserId">Id del usuario en <c>identidad.usuarios</c>.</param>
/// <param name="UserNombre">Nombre del usuario para mostrar en UI.</param>
/// <param name="ConnectionId">Id de la conexión SignalR — clave del mapa
/// del manager. Una conexión solo puede estar en un recurso a la vez:
/// llamar a <c>ViewingResource</c> sobre otro recurso releasa el anterior.</param>
/// <param name="Modo">Viewing | Editing — ver <see cref="SoftLockModo"/>.</param>
/// <param name="SinceUtc">Cuándo el usuario empezó a estar presente en
/// este recurso (para "Pedro lleva 2 min editando").</param>
/// <param name="LastSeenUtc">Último heartbeat — el worker de expiración
/// considera muerta cualquier entry con <c>LastSeenUtc &lt; now - TTL</c>.</param>
public sealed record SoftLockEntry(
    string Entidad,
    Guid EntidadId,
    Guid EmpresaId,
    Guid UserId,
    string UserNombre,
    string ConnectionId,
    SoftLockModo Modo,
    DateTimeOffset SinceUtc,
    DateTimeOffset LastSeenUtc);

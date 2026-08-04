namespace Millet.Identidad.Application.Roles;

/// <summary>
/// Shape público de un <c>Rol</c> retornado por endpoints de
/// <c>/api/v1/identidad/roles</c> (F-Admin-PR3.2).
/// </summary>
public sealed record RolResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Descripcion,
    bool EsDelSistema,
    bool Activo,
    int Version);

/// <summary>
/// Shape público de la asociación <c>RolGrupoEntraId</c> (F-Admin-PR3.2).
/// </summary>
public sealed record RolGrupoEntraIdResponse(
    Guid Id,
    Guid RolId,
    string ObjectId,
    string Nombre);

/// <summary>
/// Detalle de un rol con sus permisos asociados y los grupos de Entra ID
/// mapeados. Usado por <c>GET /api/v1/identidad/roles/{id}</c>.
/// </summary>
public sealed record RolDetalleResponse(
    RolResponse Rol,
    IReadOnlyList<Guid> PermisoIds,
    IReadOnlyList<RolGrupoEntraIdResponse> GruposEntraId);

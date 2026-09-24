namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Shape público de un <c>Usuario</c> retornado por endpoints de
/// <c>/api/v1/identidad/usuarios</c> (F-Admin-PR4.2).
///
/// <para>
/// <c>EntraOid</c> SÍ se expone en este shape (a diferencia del listado
/// catálogo <c>UsuarioListItem</c> que es read-only para selectores de
/// UI): la pantalla admin necesita verlo para distinguir entre usuarios
/// reales (oid GUID) y placeholders dev (<c>dev-*</c>). El permiso
/// <c>identidad.usuarios.leer</c> que protege el endpoint ya restringe
/// la exposición a admins.
/// </para>
/// </summary>
public sealed record UsuarioResponse(
    Guid Id,
    string Email,
    string EntraOid,
    string Nombre,
    Guid? DepartamentoId,
    bool Activo,
    int Version,
    Guid? EmpleadoId = null);

/// <summary>
/// Página de usuarios (F-Admin-PR4.2). Mismo shape que <c>ListarRolesResponse</c>.
/// </summary>
public sealed record ListarUsuariosResponse(
    IReadOnlyList<UsuarioResponse> Items,
    int Total);

/// <summary>
/// Shape público de la asignación <c>UsuarioEmpresaRol</c> (F-Admin-PR4.2).
/// </summary>
public sealed record UsuarioEmpresaRolResponse(
    Guid Id,
    Guid UsuarioId,
    Guid EmpresaId,
    Guid RolId,
    DateTimeOffset FechaAsignacion);

/// <summary>
/// Asignación expandida con el RFC de la empresa y el código del rol —
/// usada por el detalle del usuario para que la UI no tenga que hacer
/// joins adicionales contra empresas + roles.
/// </summary>
public sealed record AsignacionDetalleResponse(
    Guid Id,
    Guid EmpresaId,
    string EmpresaRfc,
    Guid RolId,
    string RolCodigo,
    DateTimeOffset FechaAsignacion);

/// <summary>
/// Detalle de un usuario con sus asignaciones activas (F-Admin-PR4.2).
/// </summary>
public sealed record UsuarioDetalleResponse(
    UsuarioResponse Usuario,
    IReadOnlyList<AsignacionDetalleResponse> Asignaciones);

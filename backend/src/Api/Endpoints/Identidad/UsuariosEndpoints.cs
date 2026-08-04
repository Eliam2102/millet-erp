using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Endpoints.Catalogos;
using Millet.Api.Web;
using Millet.Identidad.Application.Usuarios;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;

namespace Millet.Api.Endpoints.Identidad;

/// <summary>
/// Endpoints HTTP del catálogo de usuarios + CRUD admin
/// (F-Admin-PR4.2). Coexisten dos "vistas":
///
/// <list type="bullet">
///   <item><b>Catálogo</b> <c>GET /</c> (legacy B.1, sin <c>EntraOid</c>):
///         selectores de UI para "asignar requisitante", "designar
///         aprobador", etc. Shape <see cref="UsuarioListItem"/>.</item>
///   <item><b>Admin</b> <c>GET /admin</c> (F-Admin-PR4.2, con
///         <c>EntraOid</c>): pantalla de administración de usuarios.
///         Shape <see cref="UsuarioResponse"/>.</item>
/// </list>
///
/// <para>
/// Se mantienen separados para evitar romper consumers existentes del
/// catálogo y para tener policies de exposición distintas por shape.
/// El sub-recurso <c>asignaciones</c> también vive aquí.
/// </para>
/// </summary>
public static class UsuariosEndpoints
{
    private const int LimitMax = 200;

    public static IEndpointRouteBuilder MapUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identidad/usuarios").WithTags("Identidad");

        // --- CATÁLOGO (legacy B.1, sin EntraOid) ---
        group.MapGet("/", async (
            [FromQuery] bool? activo,
            [FromQuery] Guid? departamentoId,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IdentidadDbContext db,
            CancellationToken ct) =>
        {
            var off = offset is < 0 ? 0 : offset ?? 0;
            var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);

            IQueryable<Usuario> query = db.Usuarios.AsNoTracking();
            if (activo is bool a) query = query.Where(u => u.Activo == a);
            if (departamentoId is Guid d) query = query.Where(u => u.DepartamentoId == d);
            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(u => u.Email.Contains(q) || u.Nombre.Contains(q));
            }

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderBy(u => u.Nombre)
                .Skip(off).Take(lim)
                .Select(u => new UsuarioListItem(
                    u.Id, u.Email, u.Nombre, u.DepartamentoId, u.Activo))
                .ToListAsync(ct);

            return Results.Ok(new PagedCatalogoResponse<UsuarioListItem>(items, off, lim, total));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadUsuariosLeer)
        .WithName("ListarUsuarios")
        .WithSummary("Listar usuarios — catálogo legacy para selectores (B.1)")
        .WithDescription(
            "Shape catálogo SIN `entraOid`. Filtros opcionales: `activo`, " +
            "`departamentoId`, `q`. Permiso: `identidad.usuarios.leer`.")
        .Produces<PagedCatalogoResponse<UsuarioListItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- ADMIN LIST (F-Admin-PR4.2, con EntraOid) ---
        group.MapGet("/admin", async (
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] bool? soloActivos,
            [FromQuery] Guid? empresaId,
            [FromQuery] Guid? rolId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarUsuariosQuery(
                    Offset: offset ?? 0,
                    Limit: limit ?? 50,
                    SoloActivos: soloActivos,
                    EmpresaId: empresaId,
                    RolId: rolId),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadUsuariosLeer)
        .WithName("ListarUsuariosAdmin")
        .WithSummary("Listar usuarios (admin) con shape completo")
        .Produces<ListarUsuariosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- CREATE ---
        group.MapPost("/", async (
            [FromBody] CrearUsuarioCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/identidad/usuarios/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadUsuariosCrear)
        .WithName("CrearUsuario")
        .WithSummary("Crear usuario nuevo")
        .Produces<UsuarioResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // --- DETALLE ---
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ObtenerUsuarioQuery(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadUsuariosLeer)
        .WithName("ObtenerUsuario")
        .WithSummary("Detalle de usuario con asignaciones expandidas")
        .Produces<UsuarioDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- PATCH ---
        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarUsuarioPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new ActualizarUsuarioCommand(
                id,
                payload.Email,
                payload.NombreCompleto,
                payload.DepartamentoId,
                payload.LimpiarDepartamento ?? false);
            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadUsuariosEditar)
        .WithName("ActualizarUsuario")
        .WithSummary("PATCH parcial sobre usuario")
        .Produces<UsuarioResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // --- DESACTIVAR ---
        group.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarUsuarioCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadUsuariosDesactivar)
        .WithName("DesactivarUsuario")
        .WithSummary("Desactivar usuario (soft-delete)")
        .Produces<UsuarioResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- REACTIVAR ---
        group.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ReactivarUsuarioCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadUsuariosEditar)
        .WithName("ReactivarUsuario")
        .WithSummary("Reactivar usuario previamente desactivado")
        .Produces<UsuarioResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- ASIGNAR ROL EN EMPRESA ---
        group.MapPost("/{id:guid}/asignaciones", async (
            Guid id,
            [FromBody] AsignarRolPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AsignarRolAUsuarioCommand(id, payload.EmpresaId, payload.RolId),
                ct);
            return Results.Created(
                $"/api/v1/identidad/usuarios/asignaciones/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadAsignacionesAdministrar)
        .WithName("AsignarRolAUsuario")
        .WithSummary("Asignar un rol al usuario en una empresa")
        .Produces<UsuarioEmpresaRolResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // --- REVOCAR ASIGNACIÓN (sub-ruta plana por id de la asignación) ---
        group.MapDelete("/asignaciones/{usuarioEmpresaRolId:guid}", async (
            Guid usuarioEmpresaRolId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new RevocarRolDeUsuarioCommand(usuarioEmpresaRolId), ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadAsignacionesAdministrar)
        .WithName("RevocarRolDeUsuario")
        .WithSummary("Borrar la asignación usuario↔empresa↔rol")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Payload del PATCH /usuarios/{id}.</summary>
    public sealed record ActualizarUsuarioPayload(
        string? Email,
        string? NombreCompleto,
        Guid? DepartamentoId,
        bool? LimpiarDepartamento);

    /// <summary>Payload del POST /usuarios/{id}/asignaciones.</summary>
    public sealed record AsignarRolPayload(Guid EmpresaId, Guid RolId);
}

/// <summary>
/// Shape público del usuario en el listado catálogo (legacy B.1).
/// Sin <c>entraOid</c>: ese campo es interno (referencia a Microsoft
/// Entra ID) y no debe llegar al frontend ni a logs externos.
/// </summary>
public sealed record UsuarioListItem(
    Guid Id,
    string Email,
    string Nombre,
    Guid? DepartamentoId,
    bool Activo);

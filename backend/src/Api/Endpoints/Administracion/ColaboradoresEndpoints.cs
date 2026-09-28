using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Abstractions;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Application.Colaboradores;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Alta unificada de colaborador (F1-ADM-01, plan 15, F3):
/// <c>POST /api/v1/admin/colaboradores</c> crea el Empleado y, según el
/// camino de acceso, su Usuario con rol y sucursal en una sola operación.
///
/// <para>
/// Permisos: <c>admin.empleados.gestionar</c> siempre. Si el colaborador
/// tendrá acceso al ERP, además <c>identidad.usuarios.crear</c> e
/// <c>identidad.asignaciones.administrar</c> (los mismos que pedirían las
/// altas por separado).
/// </para>
/// </summary>
public static class ColaboradoresEndpoints
{
    public static IEndpointRouteBuilder MapColaboradoresEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/colaboradores")
            .WithTags("Administracion")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpleadosGestionar);

        group.MapPost("/", async (
            [FromBody] AltaColaboradorCommand command,
            IMediator mediator,
            IAuthorizationService authorization,
            HttpContext httpContext,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            await EmpleadoSucursalScope.VerificarSucursalAsync(
                command.SucursalId, currentUser, permisos, usuarioSucursales, ct);
            if (command.Acceso != TipoAccesoColaborador.SinAcceso)
            {
                foreach (var permiso in new[]
                {
                    PermisosCanonicos.IdentidadUsuariosCrear,
                    PermisosCanonicos.IdentidadAsignacionesAdministrar,
                })
                {
                    var auth = await authorization.AuthorizeAsync(
                        httpContext.User, resource: null, new PermissionRequirement(permiso));
                    if (!auth.Succeeded) return Results.Forbid();
                }
            }

            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/admin/empleados/{response.Empleado.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AltaColaborador")
        .WithSummary("Alta unificada de colaborador (empleado + acceso)")
        .WithDescription(
            "Caminos de `acceso`: 0 = sin acceso al ERP, 1 = ya tiene cuenta Microsoft, " +
            "2 = cuenta Microsoft nueva (requiere `emailContacto`; el usuario queda en " +
            "ProvisionandoCuenta; la creación y el correo requieren adaptadores reales). Con acceso, " +
            "`rolId` es obligatorio: el rol sugerido del puesto no se asigna automáticamente.")
        .Produces<AltaColaboradorResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // === Acceso del colaborador (F4) ===

        group.MapPost("/{empleadoId:guid}/acceso", async (
            Guid empleadoId,
            [FromBody] DarAccesoColaboradorPayload payload,
            IMediator mediator,
            IAuthorizationService authorization,
            HttpContext httpContext,
            CompartidoDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            await EmpleadoSucursalScope.VerificarEmpleadoAsync(
                empleadoId, db, currentUser, permisos, usuarioSucursales, ct);
            foreach (var permiso in new[]
            {
                PermisosCanonicos.IdentidadUsuariosCrear,
                PermisosCanonicos.IdentidadAsignacionesAdministrar,
            })
                if (!await TienePermisoAsync(authorization, httpContext, permiso))
                    return Results.Forbid();
            var result = await mediator.Send(new DarAccesoColaboradorCommand(
                empleadoId, payload.Acceso, payload.CorreoCorporativo,
                payload.RolId, payload.EmailContacto), ct);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DarAccesoColaborador")
        .WithSummary("Dar acceso a un empleado que nació sin usuario")
        .Produces<AltaColaboradorResponse>()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{empleadoId:guid}/acceso", async (
            Guid empleadoId,
            IMediator mediator,
            CompartidoDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            await EmpleadoSucursalScope.VerificarEmpleadoAsync(
                empleadoId, db, currentUser, permisos, usuarioSucursales, ct);
            return Results.Ok(await mediator.Send(new ObtenerAccesoColaboradorQuery(empleadoId), ct));
        })
        .WithName("ObtenerAccesoColaborador")
        .WithSummary("Estado del acceso al ERP de un colaborador")
        .Produces<EstadoAccesoColaboradorResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{empleadoId:guid}/acceso/reintentar", async (
            Guid empleadoId,
            IMediator mediator,
            IAuthorizationService authorization,
            HttpContext httpContext,
            CompartidoDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            if (!await TienePermisoAsync(authorization, httpContext, PermisosCanonicos.IdentidadUsuariosCrear))
                return Results.Forbid();
            await EmpleadoSucursalScope.VerificarEmpleadoAsync(
                empleadoId, db, currentUser, permisos, usuarioSucursales, ct);
            return Results.Ok(await mediator.Send(new ReintentarProvisionColaboradorCommand(empleadoId), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReintentarProvisionColaborador")
        .WithSummary("Vuelve a pedir la cuenta Microsoft nueva después de un error de provisión")
        .Produces<EstadoAccesoColaboradorResponse>()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{empleadoId:guid}/acceso/reenviar", async (
            Guid empleadoId,
            IMediator mediator,
            IAuthorizationService authorization,
            HttpContext httpContext,
            CompartidoDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            if (!await TienePermisoAsync(authorization, httpContext, PermisosCanonicos.IdentidadUsuariosCrear))
                return Results.Forbid();
            await EmpleadoSucursalScope.VerificarEmpleadoAsync(
                empleadoId, db, currentUser, permisos, usuarioSucursales, ct);
            return Results.Ok(await mediator.Send(new ReenviarAccesoColaboradorCommand(empleadoId), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReenviarAccesoColaborador")
        .WithSummary("Genera una contraseña temporal nueva y la envía al correo de contacto")
        .Produces<EstadoAccesoColaboradorResponse>()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record DarAccesoColaboradorPayload(
        TipoAccesoColaborador Acceso,
        string CorreoCorporativo,
        Guid? RolId = null,
        string? EmailContacto = null);

    private static async Task<bool> TienePermisoAsync(
        IAuthorizationService authorization, HttpContext httpContext, string permiso)
    {
        var auth = await authorization.AuthorizeAsync(
            httpContext.User, resource: null, new PermissionRequirement(permiso));
        return auth.Succeeded;
    }
}

using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Empleados;
using Millet.Administracion.Application.Abstractions;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Application.Colaboradores;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de Empleados (ADM-PR1,
/// doc 10-catalogo-puestos-empleados).
///
/// <list type="bullet">
///   <item><c>POST   /api/v1/admin/empleados</c> — alta.</item>
///   <item><c>PATCH  /api/v1/admin/empleados/{id}</c> — patch parcial.</item>
///   <item><c>POST   /api/v1/admin/empleados/{id}/desactivar</c> — baja lógica.</item>
/// </list>
///
/// <para>Permiso: <c>admin.empleados.gestionar</c>. Lectura por
/// <c>GET /api/v1/catalogos/empleados</c>.</para>
/// </summary>
public static class EmpleadosEndpoints
{
    public static IEndpointRouteBuilder MapEmpleadosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/empleados")
            .WithTags("Administracion")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpleadosGestionar);

        group.MapPost("/", async (
            [FromBody] CrearEmpleadoCommand command,
            IMediator mediator,
            CompartidoDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            await EmpleadoSucursalScope.VerificarSucursalAsync(
                command.SucursalId, currentUser, permisos, usuarioSucursales, ct);
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/admin/empleados/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearEmpleado")
        .WithSummary("Crear empleado")
        .Produces<EmpleadoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarEmpleadoPayload payload,
            IMediator mediator,
            CompartidoDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            await EmpleadoSucursalScope.VerificarEmpleadoAsync(
                id, db, currentUser, permisos, usuarioSucursales, ct);
            // Transferir o quitar la sucursal laboral exige alcance también sobre el destino.
            if (payload.SucursalId is not null || payload.LimpiarSucursal)
            {
                await EmpleadoSucursalScope.VerificarSucursalAsync(
                    payload.LimpiarSucursal ? null : payload.SucursalId,
                    currentUser, permisos, usuarioSucursales, ct);
            }
            var response = await mediator.Send(
                new ActualizarColaboradorCommand(new ActualizarEmpleadoCommand(
                    id,
                    payload.Nombre,
                    payload.Email,
                    payload.LimpiarEmail,
                    payload.PuestoId,
                    payload.LimpiarPuesto,
                    payload.JefeDirectoId,
                    payload.LimpiarJefeDirecto,
                    payload.SucursalId,
                    payload.LimpiarSucursal,
                    payload.DepartamentoId,
                    payload.LimpiarDepartamento,
                    payload.UsuarioId,
                    payload.LimpiarUsuario,
                    payload.CodigoNomina,
                    payload.LimpiarCodigoNomina,
                    payload.EmailContacto,
                    payload.LimpiarEmailContacto)), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarEmpleado")
        .WithSummary("PATCH parcial sobre empleado")
        .Produces<EmpleadoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CompartidoDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            await EmpleadoSucursalScope.VerificarEmpleadoAsync(
                id, db, currentUser, permisos, usuarioSucursales, ct);
            var response = await mediator.Send(new DesactivarColaboradorCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarEmpleado")
        .WithSummary("Desactivar empleado (baja lógica, idempotente)")
        .Produces<EmpleadoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            IMediator mediator,
            CompartidoDbContext db,
            ICurrentUserContext currentUser,
            ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort usuarioSucursales,
            CancellationToken ct) =>
        {
            await EmpleadoSucursalScope.VerificarEmpleadoAsync(
                id, db, currentUser, permisos, usuarioSucursales, ct);
            var response = await mediator.Send(new ReactivarColaboradorCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReactivarEmpleado")
        .WithSummary("Reactivar empleado (recontratación, idempotente)")
        .Produces<EmpleadoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record ActualizarEmpleadoPayload(
        string? Nombre = null,
        string? Email = null,
        bool LimpiarEmail = false,
        Guid? PuestoId = null,
        bool LimpiarPuesto = false,
        Guid? JefeDirectoId = null,
        bool LimpiarJefeDirecto = false,
        Guid? SucursalId = null,
        bool LimpiarSucursal = false,
        Guid? DepartamentoId = null,
        bool LimpiarDepartamento = false,
        Guid? UsuarioId = null,
        bool LimpiarUsuario = false,
        string? CodigoNomina = null,
        bool LimpiarCodigoNomina = false,
        string? EmailContacto = null,
        bool LimpiarEmailContacto = false);
}

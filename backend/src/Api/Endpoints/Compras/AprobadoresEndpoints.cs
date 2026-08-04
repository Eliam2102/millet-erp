using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compras.Application.Aprobadores;
using Millet.Compras.Domain;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Compras;

/// <summary>
/// Endpoints HTTP para administración de aprobadores por departamento
/// (F9-PR1, A1 multidimensional). El cliente captura su matriz de
/// aprobadores vía estos endpoints — no hay seed estático coordinado
/// con el cliente, lo cual permite que mantenga la matriz vigente
/// solo (rotaciones, vacaciones, promociones) sin pasar por dev.
///
/// <para>Permiso: <c>compras.aprobadores.administrar</c>.</para>
/// </summary>
public static class AprobadoresEndpoints
{
    public static IEndpointRouteBuilder MapAprobadoresEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/compras/aprobadores")
            .WithTags("Compras")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasAprobadoresAdministrar);

        group.MapPost("/", async (
            [FromBody] DesignarAprobadorRequest request,
            IMediator mediator,
            IdentidadDbContext identidad,
            CancellationToken cancellationToken) =>
        {
            // Validación cross-module: usuario existe y está activo. Compras
            // no debe conocer al módulo Identidad — la validación se hace
            // aquí (en el host integrador), no en el handler.
            var existeActivo = await identidad.Usuarios
                .AnyAsync(u => u.Id == request.UsuarioId && u.Activo, cancellationToken);
            if (!existeActivo)
            {
                throw new EntityNotFoundException(
                    "USUARIO_NO_ENCONTRADO",
                    $"No existe usuario activo con id '{request.UsuarioId}'.");
            }

            var response = await mediator.Send(
                new DesignarAprobadorCommand(
                    request.DepartamentoId, request.Rol, request.UsuarioId, request.Motivo),
                cancellationToken);
            return Results.Created($"/api/v1/compras/aprobadores/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesignarAprobador")
        .WithSummary("Designar aprobador (cierra el vigente anterior si existe)")
        .WithDescription(
            "Asigna a un usuario como aprobador del rol indicado en el " +
            "departamento. Si ya existe un aprobador vigente para " +
            "`(empresa, depto, rol)`, su `vigenteHasta` se setea a now() y " +
            "se inserta la nueva asignación con `vigenteDesde = now()`. " +
            "Re-designar al mismo usuario es no-op idempotente. Header " +
            "`Idempotency-Key` obligatorio.")
        .Produces<DesignarAprobadorResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async (
            [FromQuery] Guid? departamentoId,
            [FromQuery] RolAprobador? rol,
            [FromQuery] Guid? usuarioId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarAprobadoresVigentesQuery(departamentoId, rol, usuarioId),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithName("ListarAprobadoresVigentes")
        .WithSummary("Listar aprobadores vigentes")
        .WithDescription(
            "Devuelve las asignaciones activas (`vigenteHasta IS NULL`) " +
            "de la empresa del JWT. Filtros opcionales por `departamentoId`, " +
            "`rol` (0=JefeDpto, 1=JefeAlmacen, 2=AutorizadorN2) y `usuarioId`. " +
            "Sin filtros, lista la matriz completa.")
        .Produces<IReadOnlyList<AprobadorVigenteResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(new RevocarAprobadorCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RevocarAprobador")
        .WithSummary("Revocar aprobador (cerrar vigencia)")
        .WithDescription(
            "Cierra la asignación con `vigenteHasta = now()`. Idempotente: " +
            "si ya está cerrada, devuelve 204 sin hacer cambios. 404 si el " +
            "id no existe en la empresa actual. Header `Idempotency-Key` " +
            "obligatorio.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/historico", async (
            [FromQuery] Guid? departamentoId,
            [FromQuery] RolAprobador? rol,
            [FromQuery] Guid? usuarioId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarAprobadoresHistoricoQuery(departamentoId, rol, usuarioId),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithName("ListarAprobadoresHistorico")
        .WithSummary("Histórico de aprobadores (auditoría)")
        .WithDescription(
            "Lista las asignaciones vigentes Y cerradas, ordenadas por " +
            "`vigenteDesde DESC`. Tope de 500 filas. Requiere AL MENOS UN " +
            "filtro (`departamentoId`, `rol` o `usuarioId`) para evitar " +
            "full scan del histórico → 422 `FILTRO_OBLIGATORIO` sin filtros.")
        .Produces<IReadOnlyList<AprobadorHistoricoResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Body del POST /aprobadores.</summary>
    public sealed record DesignarAprobadorRequest(
        Guid DepartamentoId,
        RolAprobador Rol,
        Guid UsuarioId,
        string? Motivo);
}

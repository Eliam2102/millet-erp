using MediatR;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CentrosCosto.Application.Asignaciones;
using Millet.Identidad.Application;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.CentrosCosto;

/// <summary>
/// Endpoints del alcance usuario → máquinas (CECO-PR6, 01-diseno §7) bajo
/// <c>/api/v1/centros-costo/asignaciones</c>. Marcar cualquier nivel es un
/// atajo de captura: el backend expande a Dim3 vivas y guarda hojas.
///
/// <para>
/// SIN If-Match (precedente: el PUT <c>usuario-alcances</c> de Cajas):
/// ADR-0012 aplica a edición de entidades; el marcado es una operación de
/// conjunto last-write-wins. Mutación con Idempotency-Key (ADR-0020).
/// </para>
/// </summary>
public static class CentrosCostoAsignacionesEndpoints
{
    public static IEndpointRouteBuilder MapCentrosCostoAsignacionesEndpoints(this IEndpointRouteBuilder app)
    {
        var administrar = PermissionPolicyProvider.Prefix
            + PermisosCanonicos.CentrosCostoAsignacionesAdministrar;

        var group = app
            .MapGroup("/api/v1/centros-costo/asignaciones")
            .WithTags("CentrosCosto")
            .RequireAuthorization();

        group.MapGet("/{usuarioId:guid}/arbol", async (
            Guid usuarioId,
            IMediator mediator,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
            CancellationToken ct) =>
        {
            var arbol = await mediator.Send(new ObtenerArbolAsignacionQuery(usuarioId), ct);

            // esAlcanceTotal del usuario SELECCIONADO (no el actual): se
            // resuelve AQUÍ, no en el módulo — el endpoint es el composition
            // root que ya puentea Identidad; CentrosCosto sigue sin
            // referencia a Identidad (#620). Molde cache→loader de
            // RequisicionesEndpoints.
            if (currentEmpresa.Current is not Guid empresaId)
                throw new ForbiddenException(
                    "EMPRESA_NO_SELECCIONADA",
                    "El usuario no tiene una empresa seleccionada en el JWT actual.");

            var permisos = await permissionCache.GetAsync(usuarioId, empresaId, ct);
            if (permisos is null)
            {
                permisos = await permissionLoader.LoadForUserInEmpresaAsync(usuarioId, empresaId, ct);
                await permissionCache.SetAsync(usuarioId, empresaId, permisos, ct);
            }

            var esAlcanceTotal = permisos.Contains(PermisosCanonicos.CentrosCostoDim3LeerTodos);
            return Results.Ok(arbol with { EsAlcanceTotal = esAlcanceTotal });
        })
        .RequireAuthorization(administrar)
        .WithName("ObtenerArbolAsignacionCeCo")
        .WithSummary("Árbol de asignación de 5 niveles con tri-estado calculado y barra resumen (full-tree)")
        .Produces<ArbolAsignacionResponse>(StatusCodes.Status200OK);

        group.MapPost("/{usuarioId:guid}/marcar", async (
            Guid usuarioId,
            MarcarAlcanceRequest body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new MarcarAlcanceCommand(usuarioId, body.Nivel, body.NodoId, body.GrupoId, body.Asignar), ct);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("MarcarAlcanceCeCo")
        .WithSummary("Marca/desmarca un nodo del árbol de asignación; el backend expande a máquinas vivas (§7)")
        .Produces<MarcarAlcanceResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        return app;
    }

    /// <summary>Cuerpo del marcado; el usuario va en la ruta.</summary>
    public sealed record MarcarAlcanceRequest(
        NivelAlcance Nivel,
        Guid NodoId,
        Guid? GrupoId,
        bool Asignar);
}

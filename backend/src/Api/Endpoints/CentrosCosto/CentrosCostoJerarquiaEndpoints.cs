using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CentrosCosto;

/// <summary>
/// Consulta jerárquica lazy (árbol de configuración) y búsqueda del
/// selector de Dim3/"Máquina" (modelo Dim, CECO-PR4), bajo
/// <c>/api/v1/centros-costo/*</c>. El API habla Dim (nodoTipo =
/// raiz|dim1|dim2); el FE traduce a la etiqueta contextual. Solo lecturas
/// — permiso <c>centros_costo.catalogo.leer</c>; el filtro de alcance del
/// selector llega en CECO-PR6.
/// </summary>
public static class CentrosCostoJerarquiaEndpoints
{
    public static IEndpointRouteBuilder MapCentrosCostoJerarquiaEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.CentrosCostoCatalogoLeer;

        var group = app
            .MapGroup("/api/v1/centros-costo")
            .WithTags("CentrosCosto")
            .RequireAuthorization();

        // Hijos lazy de un nodo: cada expansión devuelve SOLO los hijos
        // inmediatos con sus conteos de descendientes vivos (mismo predicado
        // que la cascada, ADR-0049).
        group.MapGet("/jerarquia", async (
            [FromQuery] string? nodoTipo,
            [FromQuery] Guid? nodoId,
            [FromQuery] bool? incluirInactivos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (!TryParseNodoTipo(nodoTipo, out var tipo))
                return Results.Problem(
                    title: "nodoTipo inválido",
                    detail: "nodoTipo debe ser raiz|dim1|dim2.",
                    statusCode: StatusCodes.Status400BadRequest);

            var response = await mediator.Send(
                new ObtenerHijosJerarquiaCeCoQuery(tipo, nodoId, incluirInactivos ?? false), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(leer)
        .WithName("CentrosCostoJerarquia")
        .WithSummary("Hijos de un nodo del árbol de centros de costo, con conteos de descendientes vivos (CECO-PR4)")
        .Produces<IReadOnlyList<NodoCeCoDto>>(StatusCodes.Status200OK);

        // Búsqueda server-side del selector "Máquina" (typeahead top-N).
        // Dedicado — NO es el listar del admin: en CECO-PR6 el alcance del
        // usuario filtra ESTA query, no las listas planas.
        group.MapGet("/dim3/buscar", async (
            [FromQuery] string? q,
            [FromQuery] bool? incluirInactivas,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new BuscarDim3Query(q, incluirInactivas ?? false, limit ?? 20), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(leer)
        .WithName("BuscarDim3")
        .WithSummary("Búsqueda de Dim3 por clave/nombre con contexto completo para el selector (CECO-PR4; alcance en PR-6)")
        .Produces<IReadOnlyList<Dim3BusquedaItem>>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>Omitido/vacío = raíz. Acepta el nombre del enum, case-insensitive (molde SaldosEndpoints).</summary>
    private static bool TryParseNodoTipo(string? valor, out NivelNodoCeCo tipo)
    {
        if (string.IsNullOrEmpty(valor))
        {
            tipo = NivelNodoCeCo.Raiz;
            return true;
        }

        return Enum.TryParse(valor, ignoreCase: true, out tipo)
            && Enum.IsDefined(tipo);
    }
}

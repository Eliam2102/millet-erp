using MediatR;
using Millet.Api.Auth;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Compras.Oc;

/// <summary>
/// Selector ABIERTO de CC-Máquina (Dim3) para la captura de la línea MANUAL de
/// OC (Fase E PR3, ADR-0050 §2 "elegido-abierto-por-proxy"). Devuelve TODAS las
/// máquinas activas SIN filtro de alcance: el comprador compra para cualquier
/// área, no es el dueño del gasto.
///
/// <para>El backend es quien abre el selector, no el FE: este endpoint está
/// gateado por <c>compras.ordenes.crear-sin-rq</c> — el mismo permiso que ya
/// identifica a quien puede levantar OCs sin requisición (FOC11). Un usuario sin
/// ese permiso recibe 403; no puede saltarse el alcance eligiendo la URL. La
/// query <see cref="BuscarDim3AbiertoQuery"/> (PR1) no usa el evaluador de
/// alcance, así que CentrosCosto sigue dependency-free. Contrasta con
/// <c>/api/v1/centros-costo/dim3/buscar</c> (FILTRADO por alcance, para la RQ).</para>
///
/// <para>Es la PRIMERA materialización del patrón captura-por-proxy: el vale
/// (PR5) tendrá su gemelo bajo Almacén gateado por <c>almacen.salidas.por-vale</c>.</para>
/// </summary>
public static class Dim3BuscarAbiertoEndpoint
{
    public static IEndpointRouteBuilder MapDim3BuscarAbiertoEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/compras/ordenes/dim3/buscar", async (
            IMediator mediator,
            CancellationToken ct,
            string? q,
            bool? incluirInactivas,
            int? limit) =>
        {
            var items = await mediator.Send(
                new BuscarDim3AbiertoQuery(q, incluirInactivas ?? false, limit ?? 20), ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrearSinRq)
        .WithName("BuscarDim3AbiertoOc")
        .WithTags("Compras Ordenes")
        .WithSummary("Selector abierto de CC-Máquina para la línea manual de OC (sin filtro de alcance, gateado por crear-sin-rq)")
        .Produces<IReadOnlyList<Dim3BusquedaItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

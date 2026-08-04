using MediatR;
using Millet.Api.Auth;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Salidas;

/// <summary>
/// Selector ABIERTO de CC-Máquina (Dim3) para la captura de la línea de un VALE
/// (salida manual sin RQ; Fase E PR5, ADR-0050 §2 "elegido-abierto-por-proxy").
/// Devuelve TODAS las máquinas activas SIN filtro de alcance: el almacenista que
/// despacha un vale urgente imputa a cualquier máquina, no es el dueño del gasto.
///
/// <para>El backend es quien abre el selector, no el FE: este endpoint está
/// gateado por <c>almacen.salidas.por-vale</c> — el mismo permiso que ya
/// identifica a quien puede registrar salidas urgentes por vale. Un usuario sin
/// ese permiso recibe 403; no puede saltarse el alcance eligiendo la URL. La
/// query <see cref="BuscarDim3AbiertoQuery"/> (PR1) no usa el evaluador de
/// alcance, así que CentrosCosto sigue dependency-free.</para>
///
/// <para>Gemelo bajo Almacén del <c>/api/v1/compras/ordenes/dim3/buscar</c> de
/// OC (PR3) — SEGUNDA materialización del patrón captura-por-proxy. La salida
/// desde RQ (variante A) NO usa este selector: hereda el CC de la línea de RQ,
/// bloqueado.</para>
/// </summary>
public static class Dim3BuscarAbiertoSalidaEndpoint
{
    public static IEndpointRouteBuilder MapDim3BuscarAbiertoSalidaEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/almacen/salidas/dim3/buscar", async (
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
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasPorVale)
        .WithName("BuscarDim3AbiertoSalida")
        .WithTags("Almacen")
        .WithSummary("Selector abierto de CC-Máquina para la línea de un vale (sin filtro de alcance, gateado por almacen.salidas.por-vale)")
        .Produces<IReadOnlyList<Dim3BusquedaItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

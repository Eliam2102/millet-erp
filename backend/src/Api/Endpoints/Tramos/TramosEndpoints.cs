using MediatR;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compartido.Application.Tramos;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Tramos;

namespace Millet.Api.Endpoints.Tramos;

/// <summary>M-303: interruptores de tramo. Todos ven el estado (la captura muestra el aviso); detener y reanudar con permiso, y reanudar otra persona.</summary>
public static class TramosEndpoints
{
    public sealed record MotivoTramoBody(string Motivo);

    public static IEndpointRouteBuilder MapTramosEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/tramos").WithTags("Tramos").RequireAuthorization();
        var gestionar = PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoTramosGestionar;
        g.MapGet("", async (IMediator mediator, CancellationToken ct) => Results.Ok(await mediator.Send(new ListarTramosQuery(), ct)));
        g.MapPost("/{clave}/detener", async (string clave, MotivoTramoBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DetenerTramoCommand(clave, body.Motivo), ct)))
            .RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapPost("/{clave}/reanudar", async (string clave, MotivoTramoBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ReanudarTramoCommand(clave, body.Motivo), ct)))
            .RequireAuthorization(gestionar).WithMetadata(new RequireIdempotencyKeyAttribute());
        return app;
    }

    /// <summary>Filtro de captura: con el tramo detenido, toda escritura del grupo responde 409 con el aviso.</summary>
    public static RouteGroupBuilder ConTramoDeCaptura(this RouteGroupBuilder g, string clave)
    {
        g.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!HttpMethods.IsGet(http.Request.Method)
                && await http.RequestServices.GetRequiredService<ITramosOperativosPort>().DetenidoAsync(clave, http.RequestAborted))
                return Results.Problem(statusCode: 409, title: $"{TramosOperativos.Buscar(clave)?.Nombre ?? clave}: tramo detenido. La captura se reanuda cuando se autorice.",
                    extensions: new Dictionary<string, object?> { ["code"] = "TRAMO_DETENIDO", ["tramo"] = clave });
            return await next(context);
        });
        return g;
    }
}

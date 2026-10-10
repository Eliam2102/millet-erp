using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Cuentas;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Api.Endpoints.Tesoreria;

public static class ConceptosEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaConceptosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1/tesoreria/conceptos").WithTags("Tesoreria").RequireAuthorization();
        grupo.MapGet("/", async ([FromQuery] bool? incluirInactivos, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarConceptosQuery(incluirInactivos ?? false), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaCuentasVer);
        grupo.MapPost("/", async ([FromBody] ConceptoBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GuardarConceptoCommand(null, body.Nombre, body.ClasificacionFlujo), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaCuentasAdministrar);
        grupo.MapPut("/{id:guid}", async (Guid id, [FromBody] ConceptoBody body,
            [FromHeader(Name = "X-Expected-Version")] int? version, IMediator mediator, CancellationToken ct) =>
        {
            if (version is null) return Results.Problem(title: "X-Expected-Version requerido", statusCode: 428);
            return Results.Ok(await mediator.Send(new GuardarConceptoCommand(id, body.Nombre, body.ClasificacionFlujo, body.Activo, version), ct));
        }).WithMetadata(new RequireIdempotencyKeyAttribute())
          .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaCuentasAdministrar);
        return app;
    }
    public sealed record ConceptoBody(string Nombre, ClasificacionFlujo ClasificacionFlujo, bool Activo = true);
}

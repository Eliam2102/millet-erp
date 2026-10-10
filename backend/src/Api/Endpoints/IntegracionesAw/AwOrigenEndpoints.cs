using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.IntegracionesAw;

public static class AwOrigenEndpoints
{
    public sealed record CambiarOrigenPayload(string Origen);

    public static IEndpointRouteBuilder MapAwOrigenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/integraciones/aw/origen").WithTags("Integraciones A+W");
        group.MapGet("/", async (ISender sender, HttpResponse response, CancellationToken ct) =>
        {
            var estado = await sender.Send(new ObtenerAwOrigenQuery(), ct);
            response.Headers.ETag = $"\"{estado.Version}\"";
            return Results.Ok(estado);
        }).RequireAuthorization(); // La etiqueta se muestra a todos los usuarios de sincronización.

        group.MapPut("/", async ([FromBody] CambiarOrigenPayload payload,
            [FromHeader(Name = "If-Match")] string? ifMatch, ISender sender, HttpResponse response, CancellationToken ct) =>
        {
            int? version = null;
            if (!string.IsNullOrWhiteSpace(ifMatch))
            {
                if (ifMatch.Length < 3 || ifMatch[0] != '"' || ifMatch[^1] != '"'
                    || !int.TryParse(ifMatch[1..^1], out var v) || v < 0)
                    throw new BusinessRuleException("IF_MATCH_INVALIDO", "If-Match debe ser la versión (ETag) del origen de A+W.");
                version = v;
            }
            var estado = await sender.Send(new CambiarAwOrigenCommand(payload.Origen, version), ct);
            response.Headers.ETag = $"\"{estado.Version}\"";
            return Results.Ok(estado);
        }).WithMetadata(new RequireIdempotencyKeyAttribute())
          .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwAdministracionConfiguracion)
          .Produces<AwOrigenEstado>(200).ProducesProblem(403).ProducesProblem(422).ProducesProblem(409);
        return app;
    }
}

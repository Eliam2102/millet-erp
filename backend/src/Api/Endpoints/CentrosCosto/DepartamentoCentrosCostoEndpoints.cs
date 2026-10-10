using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CentrosCosto.Application.Departamentos;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.Identidad.Domain;
namespace Millet.Api.Endpoints.CentrosCosto;
public static class DepartamentoCentrosCostoEndpoints
{
    public static IEndpointRouteBuilder MapDepartamentoCentrosCostoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/centros-costo/equivalencias").WithTags("CentrosCosto").RequireAuthorization();
        group.MapGet("/{sucursalId:guid}", async (Guid sucursalId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarEquivalenciasQuery(sucursalId), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CentrosCostoCatalogoLeer);
        group.MapPut("/{sucursalId:guid}/{departamentoId:guid}", async (Guid sucursalId, Guid departamentoId,
            [FromHeader(Name = "If-Match")] string? version, EquivalenciaRequest body, IMediator mediator, CancellationToken ct) =>
        {
            if (!int.TryParse(version?.Trim('"'), out var v)) return Results.Problem(statusCode: 428, title: "Se requiere If-Match con la versión de la equivalencia (0 para nueva).");
            await mediator.Send(new GuardarEquivalenciaCommand(sucursalId, departamentoId, body.CentroCostoId, body.Observaciones, v), ct);
            return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CentrosCostoCatalogoAdministrar);
        // Administración ya tiene permiso para consultar el catálogo completo.
        group.MapGet("/opciones", async (ICentroCostoCapturaPort captura, CancellationToken ct, string? q) =>
            Results.Ok((await captura.BuscarAsync(q, true, ct)).Where(x => x.Nivel < 3)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CentrosCostoCatalogoAdministrar);
        app.MapGet("/api/v1/compras/ordenes/centros-costo/buscar", async (ICentroCostoCapturaPort captura, CancellationToken ct, string? q) =>
            Results.Ok(await captura.BuscarAsync(q, true, ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesCrearSinRq);
        app.MapGet("/api/v1/compras/requisiciones/centros-costo/buscar", async (ICentroCostoCapturaPort captura, CancellationToken ct, string? q) =>
            Results.Ok(await captura.BuscarAsync(q, false, ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesEditar);
        return app;
    }
    public sealed record EquivalenciaRequest(Guid CentroCostoId, string Observaciones);
}

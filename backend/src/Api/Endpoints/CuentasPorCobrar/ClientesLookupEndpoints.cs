using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.CuentasPorCobrar.Application.Clientes;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Lookup de clientes para bandejas y selectores del frontend CxC
/// (CXC-FE-PR2). Vive en CxC —no en Datos Maestros— para que el usuario
/// de crédito y cobranza no requiera permisos de administración de
/// catálogos (mismo criterio que <c>FacturacionCatalogosEndpoints</c>).
/// Gate: <c>lineas-credito.leer</c>, el permiso base de lectura del
/// módulo.
/// </summary>
public static class ClientesLookupEndpoints
{
    public static IEndpointRouteBuilder MapClientesLookupCxcEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/cuentas-por-cobrar/clientes-lookup", async (
            [FromQuery] string? rfc,
            [FromQuery] string? razonSocial,
            [FromQuery] string? ids,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            List<Guid>? idsParsed = null;
            if (!string.IsNullOrWhiteSpace(ids))
            {
                idsParsed = [];
                foreach (var token in ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Guid.TryParse(token, out var id))
                        return Results.Problem(
                            title: "ids inválido",
                            detail: $"'{token}' no es un GUID válido.",
                            statusCode: StatusCodes.Status400BadRequest);
                    idsParsed.Add(id);
                }
            }

            var items = await mediator.Send(
                new ClientesLookupCxcQuery(rfc, razonSocial, idsParsed, limit ?? (idsParsed is null ? 20 : idsParsed.Count)),
                cancellationToken);
            return Results.Ok(items);
        })
        .WithTags("CuentasPorCobrar")
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer)
        .WithName("ClientesLookupCxc")
        .WithSummary("Lookup de clientes (RFC / razón social / ids) para el frontend CxC")
        .Produces<IReadOnlyList<ClienteLookupCxcDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

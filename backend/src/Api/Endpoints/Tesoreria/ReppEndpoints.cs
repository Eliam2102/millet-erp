using MediatR;
using Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;
using Millet.CuentasPorPagar.Application.Cfdi.DescargarCfdiBlob;
using Millet.SharedKernel.Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Repp;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints del REPP recibido de proveedor (TES-PR8, §3.6.b / TES-4
/// sabor b): read model de pagos sin complemento con SLA de 5 días y
/// registro del REPP (UUID único + XML a blob) que publica
/// <c>tesoreria.repp-proveedor.recibido.v1</c> → CxP libera FALTA_REPP.
/// Ambos con permiso <c>tesoreria.repp.registrar</c> (§10 del diseño).
/// </summary>
public static class ReppEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaReppEndpoints(this IEndpointRouteBuilder app)
    {
        var permiso = PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaReppRegistrar;

        app.MapGet("/api/v1/tesoreria/repp-pendientes", async (
            [FromQuery] Guid? proveedorId,
            [FromQuery] bool? soloVencidos,
            [FromQuery] bool? incluirSinMetodo,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ReppPendientesQuery(
                    proveedorId, soloVencidos ?? false, incluirSinMetodo ?? true,
                    offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithTags("Tesoreria")
        .RequireAuthorization(permiso)
        .WithName("ReppPendientes")
        .Produces<PagedResponse<ReppPendienteResponse>>(StatusCodes.Status200OK);

        app.MapPost("/api/v1/tesoreria/repp-recibidos", async (
            [FromBody] RegistrarReppRecibidoCommand command,
            DocumentoSucursalScope scope,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await scope.VerificarAsync("factura_proveedor", command.FacturaProveedorId, PermisosCanonicos.TesoreriaDocumentosGestionarTodasSucursales, cancellationToken);
            if (command.Pagos is not null)
                foreach (var pago in command.Pagos)
                    await scope.VerificarAsync("pago_proveedor", pago.PagoId,
                        PermisosCanonicos.TesoreriaDocumentosGestionarTodasSucursales, cancellationToken);
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/tesoreria/repp-recibidos/{response.Id}", response);
        })
        .WithTags("Tesoreria")
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(permiso)
        .WithName("RegistrarReppRecibido")
        .Produces<ReppRecibidoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/tesoreria/cfdis-repp", async (Guid facturaProveedorId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarReppParaTesoreriaQuery(facturaProveedorId), ct)))
            .WithTags("Tesoreria").RequireAuthorization(permiso)
            .WithName("ListarCfdisReppTesoreria")
            .WithSummary("Complementos recibidos de las facturas de las sucursales permitidas")
            .Produces<IReadOnlyList<CfdiListItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        app.MapGet("/api/v1/tesoreria/cfdis-repp/{id:guid}/xml", async (Guid id, Guid facturaProveedorId, IMediator mediator, CancellationToken ct) =>
        {
            var permitidos = await mediator.Send(new ListarReppParaTesoreriaQuery(facturaProveedorId), ct);
            if (!permitidos.Any(c => c.Id == id))
                throw new ForbiddenException("REPP_NO_ACCESIBLE", "El complemento no corresponde a una factura de tus sucursales.");
            var blob = await mediator.Send(new DescargarCfdiBlobQuery(id, false), ct);
            return Results.Stream(blob.Contenido, blob.ContentType, blob.FileName);
        }).WithTags("Tesoreria").RequireAuthorization(permiso)
            .WithName("DescargarXmlReppTesoreria")
            .WithSummary("XML original de un complemento accesible desde Tesorería")
            .Produces(StatusCodes.Status200OK, contentType: "application/xml")
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Facturacion.Application.Catalogos;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Defaults del emisor + lookups de catálogo para los formularios de emisión
/// (FAC-UX-PR1). Viven en Facturación —no en Datos Maestros— para que el
/// facturista no requiera permisos de administración de catálogos; por eso
/// autorizan con <c>FacturacionFacturasEmitir</c>. Cierra los
/// PLATFORM-TODO(&lt;EmisorDefaults&gt;), (&lt;ClienteSelector&gt;) y
/// (&lt;ProductoSelector&gt;) del frontend.
/// </summary>
public static class FacturacionCatalogosEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionCatalogosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion")
            .WithTags("Facturacion")
            .RequireAuthorization(
                PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasEmitir);

        // GET: RFC/razón social/régimen del emisor + sucursal única activa.
        group.MapGet("/emisor-defaults", async (
            IMediator mediator, CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new EmisorDefaultsQuery(), cancellationToken);
            return Results.Ok(response);
        })
        .WithName("EmisorDefaults")
        .WithSummary("Defaults del emisor para el formulario de emisión de CFDI")
        .Produces<EmisorDefaultsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: lookup de clientes para el selector de receptor (ADR-0045:
        // filtros excluyentes, RFC tiene precedencia).
        group.MapGet("/catalogos/clientes", async (
            [FromQuery] string? rfc,
            [FromQuery] string? razonSocial,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(
                new ClientesLookupQuery(rfc, razonSocial, limit ?? 20), cancellationToken);
            return Results.Ok(items);
        })
        .WithName("FacturacionClientesLookup")
        .WithSummary("Lookup de clientes activos para el selector de receptor")
        .Produces<IReadOnlyList<ClienteLookupItem>>(StatusCodes.Status200OK);

        // GET: lookup de canales de venta activos para los selectores de
        // captura de pedido y emisión (FAC-ING-PR2 — catálogo administrable
        // en compartido.canales_venta; reemplaza el enum hardcodeado).
        group.MapGet("/catalogos/canales-venta", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(new CanalesVentaLookupQuery(), cancellationToken);
            return Results.Ok(items);
        })
        .WithName("FacturacionCanalesVentaLookup")
        .WithSummary("Lookup de canales de venta activos para selectores")
        .Produces<IReadOnlyList<CanalVentaLookupItem>>(StatusCodes.Status200OK);

        // GET: lookup de productos A+W para el selector de conceptos.
        group.MapGet("/catalogos/productos-aw", async (
            [FromQuery] string? referencia,
            [FromQuery] string? descripcion,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(
                new ProductosAwLookupQuery(referencia, descripcion, limit ?? 20), cancellationToken);
            return Results.Ok(items);
        })
        .WithName("FacturacionProductosAwLookup")
        .WithSummary("Lookup de productos A+W activos para el selector de conceptos")
        .Produces<IReadOnlyList<ProductoAwLookupItem>>(StatusCodes.Status200OK);

        return app;
    }
}

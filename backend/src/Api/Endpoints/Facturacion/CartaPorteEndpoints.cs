using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.CartaPorte.Catalogos;
using Millet.Facturacion.Application.CartaPorte.CrearSiguienteTramo;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;
using Millet.Facturacion.Application.CartaPorte.Queries;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de Carta Porte 3.1 del módulo Facturación (F8): emisión (T/I),
/// siguiente tramo y catálogos de vehículos/operadores.
/// <c>/api/v1/facturacion/carta-porte</c>.
/// </summary>
public static class CartaPorteEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionCartaPorteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/carta-porte")
            .WithTags("Facturacion");

        var emitir = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCartaPorteEmitir;
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCartaPorteLeer;

        // GET: bandeja de Carta Porte. B11.
        group.MapGet("/", async (
            [FromQuery] EstadoTimbrado? estado, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
        {
            var items = await mediator.Send(new BandejaCartaPorteQuery(estado, offset ?? 0, limit ?? 50), ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(leer)
        .WithName("BandejaCartaPorte")
        .WithSummary("Bandeja de Cartas Porte")
        .Produces<IReadOnlyList<CartaPorteBandejaItem>>(StatusCodes.Status200OK);

        // GET: detalle de una Carta Porte. B11.
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new CartaPorteDetalleQuery(id), ct);
            return Results.Ok(detalle);
        })
        .RequireAuthorization(leer)
        .WithName("CartaPorteDetalle")
        .WithSummary("Detalle de una Carta Porte (vehículo, operador, mercancías, tramo previo)")
        .Produces<CartaPorteDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: descarga del XML timbrado de la Carta Porte. P10 — generaliza la
        // descarga a la familia CartaPorte (la query ya la soporta); necesario
        // para observar el complemento Carta Porte 3.1 en el manual.
        group.MapGet("/{id:guid}/xml", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ComprobanteXmlQuery(id, FamiliaComprobante.CartaPorte), ct);
            return Results.File(Encoding.UTF8.GetBytes(r.Xml), "application/xml", r.NombreArchivo);
        })
        .RequireAuthorization(leer)
        .WithName("CartaPorteXml")
        .WithSummary("Descarga el XML timbrado de una Carta Porte")
        .Produces(StatusCodes.Status200OK, contentType: "application/xml")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST: emite una Carta Porte (T o I).
        group.MapPost("/", async (EmitirCartaPorteCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/facturacion/carta-porte/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("EmitirCartaPorte")
        .WithSummary("Emite una Carta Porte 3.1 (CFDI tipo T o I)")
        .Produces<EmitirCartaPorteResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST: crea la Carta Porte del siguiente tramo (referencia la previa).
        group.MapPost("/{id:guid}/siguiente-tramo", async (Guid id, SiguienteTramoRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new CrearSiguienteTramoCommand(
                id, body.TipoCfdi, body.SucursalId, body.Origen, body.Destino, body.DistanciaKm,
                body.VehiculoId, body.OperadorId, body.FechaSalida, body.FechaLlegadaEstimada,
                body.MontoServicio, body.TasaIvaServicio), ct);
            return Results.Created($"/api/v1/facturacion/carta-porte/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("CrearSiguienteTramo")
        .WithSummary("Crea la Carta Porte del siguiente tramo, referenciando la previa")
        .Produces<EmitirCartaPorteResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: catálogo de vehículos (pantalla de administración + pickers).
        group.MapGet("/vehiculos", async ([FromQuery] bool? incluirInactivos, IMediator mediator, CancellationToken ct) =>
        {
            var items = await mediator.Send(new ListarVehiculosQuery(incluirInactivos ?? false), ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(leer)
        .WithName("ListarVehiculos")
        .WithSummary("Lista el catálogo de vehículos de Carta Porte")
        .Produces<IReadOnlyList<VehiculoListItem>>(StatusCodes.Status200OK);

        // GET: catálogo de operadores (pantalla de administración + pickers).
        group.MapGet("/operadores", async ([FromQuery] bool? incluirInactivos, IMediator mediator, CancellationToken ct) =>
        {
            var items = await mediator.Send(new ListarOperadoresQuery(incluirInactivos ?? false), ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(leer)
        .WithName("ListarOperadores")
        .WithSummary("Lista el catálogo de operadores de Carta Porte")
        .Produces<IReadOnlyList<OperadorListItem>>(StatusCodes.Status200OK);

        // POST: alta de vehículo en el catálogo.
        group.MapPost("/vehiculos", async (CrearVehiculoCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/facturacion/carta-porte/vehiculos/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("CrearVehiculo")
        .WithSummary("Da de alta un vehículo para Carta Porte")
        .Produces<CatalogoCreadoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        // POST: alta de operador en el catálogo.
        group.MapPost("/operadores", async (CrearOperadorCommand command, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/facturacion/carta-porte/operadores/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("CrearOperador")
        .WithSummary("Da de alta un operador para Carta Porte")
        .Produces<CatalogoCreadoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        // PATCH: edición / activación de un vehículo del catálogo.
        group.MapPatch("/vehiculos/{id:guid}", async (Guid id, ActualizarVehiculoRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var item = await mediator.Send(new ActualizarVehiculoCommand(
                id, body.ConfigVehicular, body.AnioModelo, body.TipoPermisoSct, body.NumPermisoSct,
                body.Aseguradora, body.PolizaSeguro, body.PesoBrutoVehicular, body.Activo), ct);
            return Results.Ok(item);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("ActualizarVehiculo")
        .WithSummary("Actualiza un vehículo del catálogo (placa inmutable) o cambia su estado activo")
        .Produces<VehiculoListItem>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        // PATCH: edición / activación de un operador del catálogo.
        group.MapPatch("/operadores/{id:guid}", async (Guid id, ActualizarOperadorRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var item = await mediator.Send(new ActualizarOperadorCommand(
                id, body.Nombre, body.NumLicencia, body.Activo), ct);
            return Results.Ok(item);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("ActualizarOperador")
        .WithSummary("Actualiza un operador del catálogo (RFC inmutable) o cambia su estado activo")
        .Produces<OperadorListItem>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Body del siguiente tramo (el id de la previa viene en la ruta).</summary>
    public sealed record SiguienteTramoRequest(
        string TipoCfdi,
        Guid SucursalId,
        string Origen,
        string Destino,
        decimal DistanciaKm,
        Guid VehiculoId,
        Guid OperadorId,
        DateTimeOffset FechaSalida,
        DateTimeOffset FechaLlegadaEstimada,
        decimal MontoServicio,
        decimal? TasaIvaServicio);

    /// <summary>
    /// Body del PATCH de vehículo (el id viene en la ruta). Si viene
    /// <c>ConfigVehicular</c> se reemplazan todos los datos editables;
    /// <c>Activo</c> activa/desactiva.
    /// </summary>
    public sealed record ActualizarVehiculoRequest(
        string? ConfigVehicular = null,
        int? AnioModelo = null,
        string? TipoPermisoSct = null,
        string? NumPermisoSct = null,
        string? Aseguradora = null,
        string? PolizaSeguro = null,
        decimal? PesoBrutoVehicular = null,
        bool? Activo = null);

    /// <summary>Body del PATCH de operador (el id viene en la ruta).</summary>
    public sealed record ActualizarOperadorRequest(
        string? Nombre = null,
        string? NumLicencia = null,
        bool? Activo = null);
}

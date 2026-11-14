using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.CartaPorte.Catalogos;
using Millet.Facturacion.Application.CartaPorte.CorregirCartaPorte;
using Millet.Facturacion.Application.CartaPorte.CrearSiguienteTramo;
using Millet.Facturacion.Application.CartaPorte.EmitirCartaPorte;
using Millet.Facturacion.Application.CartaPorte.Queries;
using Millet.Facturacion.Application.CartaPorte.Tramos;
using Millet.Facturacion.Domain.CartaPorte;
using Millet.Facturacion.Application.Comprobantes.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
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
            [FromQuery] string? q, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new BandejaCartaPorteQuery(estado, offset ?? 0, limit ?? 50, q), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(leer)
        .WithName("BandejaCartaPorte")
        .WithSummary("Bandeja de Cartas Porte")
        .Produces<BandejaCartaPorteResponse>(StatusCodes.Status200OK);

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

        // GET: PDF de la Carta Porte con el diseño de la factura de Millet (86e3tfdx0).
        group.MapGet("/{id:guid}/pdf", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var pdf = await mediator.Send(
                new ComprobantePdfQuery(id, FormatoPdfFactura.Bilingue, FamiliaComprobante.CartaPorte), ct);
            return Results.File(pdf.Contenido, pdf.ContentType, pdf.NombreSugerido);
        })
        .RequireAuthorization(leer)
        .WithName("CartaPortePdf")
        .WithSummary("Genera el PDF de una Carta Porte con el formato de Millet")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .ProducesProblem(StatusCodes.Status404NotFound);

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
                body.MontoServicio, body.TasaIvaServicio,
                body.OrigenCodigoPostal, body.OrigenEstado,
                body.DestinoCodigoPostal, body.DestinoEstado), ct);
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
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

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
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

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

        // M-354: corrige una Carta Porte observada (timbrado fallido) con rastro; después se reemite con «Reintentar».
        group.MapPost("/{id:guid}/corregir", async (Guid id, CorregirCartaPorteRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var cambios = await mediator.Send(new CorregirCartaPorteCommand(
                id, body.Motivo, body.ActualizarEmisor, body.Origen, body.Destino, body.DistanciaKm,
                body.OrigenCodigoPostal, body.OrigenEstado, body.DestinoCodigoPostal, body.DestinoEstado,
                body.VehiculoId, body.OperadorId, body.FechaSalida, body.FechaLlegadaEstimada), ct);
            return Results.Ok(cambios);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("CorregirCartaPorte")
        .WithSummary("Corrige el emisor o los datos de un tramo observado, con rastro, antes de reemitir")
        .Produces<IReadOnlyList<CorreccionTramoResponse>>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/{id:guid}/correcciones", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CorreccionesCartaPorteQuery(id), ct)))
        .RequireAuthorization(leer)
        .WithName("CorreccionesCartaPorte")
        .WithSummary("Bitácora de correcciones de una Carta Porte")
        .Produces<IReadOnlyList<CorreccionTramoResponse>>(StatusCodes.Status200OK);

        // M-354: tramos del traslado de un pedido, con su modalidad (alcance por sucursal del pedido, ADR-0051).
        var tramos = app.MapGroup("/api/v1/facturacion/pedidos-facturables/{pedidoId:guid}/tramos").WithTags("Facturacion");

        tramos.MapGet("/", async (Guid pedidoId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarTramosPedidoQuery(pedidoId), ct)))
        .RequireAuthorization(leer)
        .WithName("ListarTramosPedido")
        .WithSummary("Tramos del traslado del pedido con su modalidad")
        .Produces<IReadOnlyList<TramoTrasladoResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        tramos.MapPost("/", async (Guid pedidoId, CrearTramoRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new CrearTramoTrasladoCommand(
                pedidoId, body.Modalidad, body.Origen, body.Destino, body.DistanciaKm, body.OrigenCodigoPostal,
                body.OrigenEstado, body.DestinoCodigoPostal, body.DestinoEstado, body.FechaSalida), ct);
            return Results.Created($"/api/v1/facturacion/pedidos-facturables/{pedidoId}/tramos", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("CrearTramoTraslado")
        .WithSummary("Agrega un tramo al traslado del pedido (vehículo propio o transportista tercero)")
        .Produces<TramoTrasladoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        tramos.MapPost("/{tramoId:guid}/documento-tercero", async (
            Guid pedidoId, Guid tramoId, DocumentoTerceroRequest body, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new RegistrarDocumentoTerceroCommand(
                pedidoId, tramoId, body.TerceroNombre, body.DocumentoTipo, body.TerceroRfc, body.Folio, body.Uuid, body.Fecha), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(emitir)
        .WithName("RegistrarDocumentoTercero")
        .WithSummary("Registra el documento del transportista tercero que ampara el tramo (no emite Carta Porte)")
        .Produces<TramoTrasladoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>M-354: body de corrección de una Carta Porte observada (solo cambian los campos informados).</summary>
    public sealed record CorregirCartaPorteRequest(
        string Motivo, bool ActualizarEmisor = false, string? Origen = null, string? Destino = null, decimal? DistanciaKm = null,
        string? OrigenCodigoPostal = null, string? OrigenEstado = null, string? DestinoCodigoPostal = null, string? DestinoEstado = null,
        Guid? VehiculoId = null, Guid? OperadorId = null, DateTimeOffset? FechaSalida = null, DateTimeOffset? FechaLlegadaEstimada = null);

    /// <summary>M-354: body de un tramo nuevo; <c>Modalidad</c> viaja como texto (VehiculoPropio | TransportistaTercero).</summary>
    public sealed record CrearTramoRequest(
        [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ModalidadTramo>))] ModalidadTramo Modalidad,
        string Origen, string? Destino = null, decimal? DistanciaKm = null, string? OrigenCodigoPostal = null,
        string? OrigenEstado = null, string? DestinoCodigoPostal = null, string? DestinoEstado = null, DateTimeOffset? FechaSalida = null);

    /// <summary>M-354: documento del transportista tercero que ampara el tramo.</summary>
    public sealed record DocumentoTerceroRequest(
        string TerceroNombre, string DocumentoTipo, string? TerceroRfc = null, string? Folio = null, string? Uuid = null, DateOnly? Fecha = null);

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
        decimal? TasaIvaServicio,
        // Domicilio SAT del tramo nuevo (CP 3.1); sin ellos el comando falla
        // con CARTA_PORTE_DATOS_SAT_INCOMPLETOS (U1.7, 15-oct).
        string? OrigenCodigoPostal = null,
        string? OrigenEstado = null,
        string? DestinoCodigoPostal = null,
        string? DestinoEstado = null);

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

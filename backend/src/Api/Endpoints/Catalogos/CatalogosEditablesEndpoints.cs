using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Application.CondicionesPagoCatalogo;
using Millet.Catalogos.Application.Incoterms;
using Millet.Catalogos.Application.Transportistas;
using Millet.Catalogos.Application.CategoriasArticulo;
using Millet.Catalogos.Application.UnidadesMedida;
using Millet.Catalogos.Application.UsosPrincipales;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Catalogos;

/// <summary>
/// Endpoints CRUD para los catálogos editables del schema
/// <c>compartido</c> (F-Admin-PR5.2): CondicionesPago, Incoterms,
/// Transportistas, UsosPrincipales. Los GET listados read-only ya
/// existían en <see cref="CatalogosOcEndpoints"/>; esta clase agrega
/// solo las mutaciones (POST/PATCH/desactivar).
///
/// <para>
/// Permisos granulares <c>catalogos.{recurso}.gestionar</c>. Todos los
/// endpoints requieren Idempotency-Key.
/// </para>
/// </summary>
public static class CatalogosEditablesEndpoints
{
    public static IEndpointRouteBuilder MapCatalogosEditablesEndpoints(this IEndpointRouteBuilder app)
    {
        MapCondicionesPago(app);
        MapIncoterms(app);
        MapTransportistas(app);
        MapUsosPrincipales(app);
        MapUnidadesMedida(app);
        MapCategoriasArticulo(app);
        return app;
    }

    // ===== CONDICIONES DE PAGO =====
    private static void MapCondicionesPago(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos/condiciones-pago")
            .WithTags("Catalogos")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CatalogosCondicionesPagoGestionar);

        group.MapPost("/", async (
            [FromBody] CrearCondicionesPagoCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/catalogos/condiciones-pago/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearCondicionesPago")
        .Produces<CondicionesPagoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] CondicionesPagoPatch payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarCondicionesPagoCommand(id, payload.Nombre, payload.DiasCredito), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarCondicionesPago")
        .Produces<CondicionesPagoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarCondicionesPagoCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarCondicionesPago")
        .Produces<CondicionesPagoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ===== INCOTERMS =====
    private static void MapIncoterms(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos/incoterms")
            .WithTags("Catalogos")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CatalogosIncotermsGestionar);

        group.MapPost("/", async (
            [FromBody] CrearIncotermCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/catalogos/incoterms/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearIncoterm")
        .Produces<IncotermResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] IncotermPatch payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ActualizarIncotermCommand(id, payload.Nombre), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarIncoterm")
        .Produces<IncotermResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarIncotermCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarIncoterm")
        .Produces<IncotermResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ===== TRANSPORTISTAS =====
    private static void MapTransportistas(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos/transportistas")
            .WithTags("Catalogos")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CatalogosTransportistasGestionar);

        group.MapPost("/", async (
            [FromBody] CrearTransportistaCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/catalogos/transportistas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearTransportista")
        .Produces<TransportistaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] TransportistaPatch payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarTransportistaCommand(
                    id, payload.Nombre, payload.Email, payload.Telefono,
                    payload.LimpiarEmail ?? false, payload.LimpiarTelefono ?? false), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarTransportista")
        .Produces<TransportistaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarTransportistaCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarTransportista")
        .Produces<TransportistaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ===== USOS PRINCIPALES =====
    private static void MapUsosPrincipales(IEndpointRouteBuilder app)
    {
        // Reusa el permiso de Catalogos.IncotermsGestionar como agrupador
        // de usos principales también — aunque conceptualmente debería ser
        // su propio permiso, no fue solicitado en el bundle. Cae bajo
        // `compartido.catalogos.administrar` por consistencia con el
        // endpoint legacy de F9-PR1.
        var group = app.MapGroup("/api/v1/catalogos/usos-principales")
            .WithTags("Catalogos")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar);

        group.MapPost("/", async (
            [FromBody] CrearUsoPrincipalCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/catalogos/usos-principales/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearUsoPrincipal")
        .Produces<UsoPrincipalResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] UsoPrincipalPatch payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarUsoPrincipalCommand(id, payload.Nombre), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarUsoPrincipal")
        .Produces<UsoPrincipalResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarUsoPrincipalCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarUsoPrincipal")
        .Produces<UsoPrincipalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ===== CATEGORÍAS DE ARTÍCULO (patrón ADR-0046) =====
    // GET read-only vive en CatalogosOcEndpoints (compartido.catalogos.leer);
    // aquí solo mutaciones con compartido.catalogos.administrar (molde UsoPrincipal).
    private static void MapCategoriasArticulo(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos/categorias-articulo")
            .WithTags("Catalogos")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosAdministrar);

        group.MapPost("/", async (
            [FromBody] CrearCategoriaArticuloCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/catalogos/categorias-articulo/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearCategoriaArticulo")
        .Produces<CategoriaArticuloResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] CategoriaArticuloPatch payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarCategoriaArticuloCommand(id, payload.Nombre), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarCategoriaArticulo")
        .Produces<CategoriaArticuloResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarCategoriaArticuloCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarCategoriaArticulo")
        .Produces<CategoriaArticuloResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    // ===== UNIDADES DE MEDIDA (ADR-0046 Etapa 1a) =====
    private static void MapUnidadesMedida(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos/unidades-medida")
            .WithTags("Catalogos")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CatalogosUnidadesMedidaGestionar);

        group.MapPost("/", async (
            [FromBody] CrearUnidadMedidaCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/catalogos/unidades-medida/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearUnidadMedida")
        .Produces<UnidadMedidaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // PATCH parcial. Nombre/Decimales/Activo son siempre editables;
        // Dimension/FactorABase/EsBase disparan el guardrail del dominio
        // (UNIDAD_MEDIDA_EN_USO) si la unidad estuviera en uso (ADR-0046).
        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] UnidadMedidaPatch payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarUnidadMedidaCommand(
                    id, payload.Nombre, payload.Decimales,
                    payload.Dimension, payload.FactorABase, payload.EsBase), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarUnidadMedida")
        .Produces<UnidadMedidaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarUnidadMedidaCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarUnidadMedida")
        .Produces<UnidadMedidaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public sealed record CondicionesPagoPatch(string? Nombre, int? DiasCredito);
    public sealed record UnidadMedidaPatch(
        string? Nombre,
        int? Decimales,
        DimensionUnidad? Dimension,
        decimal? FactorABase,
        bool? EsBase);
    public sealed record IncotermPatch(string? Nombre);
    public sealed record TransportistaPatch(
        string? Nombre,
        string? Email,
        string? Telefono,
        bool? LimpiarEmail,
        bool? LimpiarTelefono);
    public sealed record UsoPrincipalPatch(string? Nombre);

    public sealed record CategoriaArticuloPatch(string? Nombre);
}

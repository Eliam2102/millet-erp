using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Catalogo;
using Millet.CentrosCosto.Application.Common;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CentrosCosto;

/// <summary>
/// Endpoints CRUD del catálogo de Centros de Costo (modelo Dim, CECO-PR4),
/// bajo <c>/api/v1/centros-costo/*</c> (dim1, grupos-dim2, grupos-dim3,
/// dim2, dim3). El API habla Dim — la UI traduce por contexto
/// (05-frontend §0); nunca hornea etiquetas.
///
/// <para>
/// Forma del molde Almacén (MapGroup + permisos canónicos +
/// RequireIdempotencyKey) con la semántica de concurrencia de Cajas
/// (ADR-0012): GET-por-id setea <c>ETag</c>; toda mutación sobre entidad
/// existente exige <c>If-Match</c> (428 si falta, 409 en mismatch).
/// Unicidad → 409 (ConflictException con la clave en conflicto);
/// guardrails → 422.
/// </para>
/// </summary>
public static class CentrosCostoCatalogoEndpoints
{
    public static IEndpointRouteBuilder MapCentrosCostoCatalogoEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.CentrosCostoCatalogoLeer;
        var administrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.CentrosCostoCatalogoAdministrar;

        // ─── Dim1 (Nivel 1) ──────────────────────────────────────────────────

        var dim1 = app
            .MapGroup("/api/v1/centros-costo/dim1")
            .WithTags("CentrosCosto")
            .RequireAuthorization();

        dim1.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(new ListarDim1Query(estatus, q, off, lim), ct));
        })
        .RequireAuthorization(leer)
        .WithName("ListarDim1")
        .WithSummary("Dimensiones 1 paginadas con filtros por estatus y q (CECO-PR4)")
        .Produces<PagedResponse<Dim1Response>>(StatusCodes.Status200OK);

        dim1.MapGet("/{id:guid}", async (
            Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var dto = await mediator.Send(new ObtenerDim1PorIdQuery(id), ct);
            SetEtag(response, dto.Version);
            return Results.Ok(dto);
        })
        .RequireAuthorization(leer)
        .WithName("ObtenerDim1")
        .WithSummary("Detalle de la Dimensión 1 (ETag para If-Match)")
        .Produces<Dim1Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        dim1.MapPost("/", async (
            CrearDim1Command command, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            SetEtag(response, result.Version);
            return Results.Created($"/api/v1/centros-costo/dim1/{result.Id}", result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("CrearDim1")
        .WithSummary("Crea una Dimensión 1 (catálogo propio — sin relación con las sucursales del ERP)")
        .Produces<Dim1Response>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        dim1.MapPatch("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            EditarDim1Request body,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(new EditarDim1Command(id, version, body.Clave, body.Nombre), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("EditarDim1")
        .WithSummary("Edita clave/nombre de la Dimensión 1 (If-Match)")
        .Produces<Dim1Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        dim1.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(new DesactivarDim1Command(id, version), ct);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("DesactivarDim1")
        .WithSummary("Baja lógica en CASCADA: desactiva la Dimensión 1 y sus Dim2/Dim3 vivas (ADR-0049; If-Match)")
        .Produces<DesactivarDim1Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        dim1.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(new ReactivarDim1Command(id, version), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("ReactivarDim1")
        .WithSummary("Reactiva la Dimensión 1 (NO reactiva hijos — If-Match)")
        .Produces<Dim1Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // ─── Grupos: GrupoDim2 y GrupoDim3 (forma idéntica) ──────────────────

        MapGrupoDim<CrearGrupoDim2Command, RenombrarGrupoDim2Command, CambiarEstatusGrupoDim2Command, ObtenerGrupoDim2PorIdQuery, ListarGruposDim2Query>(
            app, "grupos-dim2", "GrupoDim2",
            (nombre) => new CrearGrupoDim2Command(nombre),
            (id, v, nombre) => new RenombrarGrupoDim2Command(id, v, nombre),
            (id, v, activar) => new CambiarEstatusGrupoDim2Command(id, v, activar),
            (id) => new ObtenerGrupoDim2PorIdQuery(id),
            (estatus, q, off, lim) => new ListarGruposDim2Query(estatus, q, off, lim),
            leer, administrar);

        MapGrupoDim<CrearGrupoDim3Command, RenombrarGrupoDim3Command, CambiarEstatusGrupoDim3Command, ObtenerGrupoDim3PorIdQuery, ListarGruposDim3Query>(
            app, "grupos-dim3", "GrupoDim3",
            (nombre) => new CrearGrupoDim3Command(nombre),
            (id, v, nombre) => new RenombrarGrupoDim3Command(id, v, nombre),
            (id, v, activar) => new CambiarEstatusGrupoDim3Command(id, v, activar),
            (id) => new ObtenerGrupoDim3PorIdQuery(id),
            (estatus, q, off, lim) => new ListarGruposDim3Query(estatus, q, off, lim),
            leer, administrar);

        // ─── Dim2 (Nivel 2) ──────────────────────────────────────────────────

        var dim2 = app
            .MapGroup("/api/v1/centros-costo/dim2")
            .WithTags("CentrosCosto")
            .RequireAuthorization();

        dim2.MapGet("/", async (
            [FromQuery] Guid? dim1Id,
            [FromQuery] Guid? grupoDim2Id,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(
                new ListarDim2Query(dim1Id, grupoDim2Id, estatus, q, off, lim), ct));
        })
        .RequireAuthorization(leer)
        .WithName("ListarDim2")
        .WithSummary("Dimensiones 2 paginadas con grupo resuelto; filtros por dim1, grupo, estatus y q (CECO-PR4)")
        .Produces<PagedResponse<Dim2ListItem>>(StatusCodes.Status200OK);

        dim2.MapGet("/{id:guid}", async (
            Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var dto = await mediator.Send(new ObtenerDim2PorIdQuery(id), ct);
            SetEtag(response, dto.Version);
            return Results.Ok(dto);
        })
        .RequireAuthorization(leer)
        .WithName("ObtenerDim2")
        .WithSummary("Detalle de la Dimensión 2 con grupo resuelto (ETag)")
        .Produces<Dim2DetalleDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        dim2.MapPost("/", async (
            CrearDim2Command command, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            SetEtag(response, result.Version);
            return Results.Created($"/api/v1/centros-costo/dim2/{result.Id}", result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("CrearDim2")
        .WithSummary("Crea una Dimensión 2 bajo una Dimensión 1 activa (padre inmutable post-alta)")
        .Produces<Dim2Response>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        dim2.MapPatch("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            EditarDim2Request body,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(
                new EditarDim2Command(id, version, body.Clave, body.Nombre, body.GrupoDim2Id), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("EditarDim2")
        .WithSummary("Edita clave/nombre/grupo — nunca el padre (If-Match)")
        .Produces<Dim2Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        dim2.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(new DesactivarDim2Command(id, version), ct);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("DesactivarDim2")
        .WithSummary("Baja lógica en CASCADA: desactiva la Dimensión 2 y sus Dim3 vivas (ADR-0049; If-Match)")
        .Produces<DesactivarDim2Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        dim2.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(new ReactivarDim2Command(id, version), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("ReactivarDim2")
        .WithSummary("Reactiva la Dimensión 2 (padre debe estar activo; NO reactiva Dim3 — If-Match)")
        .Produces<Dim2Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // ─── Dim3 (Nivel 3 — hoja) ───────────────────────────────────────────

        var dim3 = app
            .MapGroup("/api/v1/centros-costo/dim3")
            .WithTags("CentrosCosto")
            .RequireAuthorization();

        dim3.MapGet("/", async (
            [FromQuery] Guid? dim2Id,
            [FromQuery] Guid? grupoDim3Id,
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(
                new ListarDim3Query(dim2Id, grupoDim3Id, estatus, q, off, lim), ct));
        })
        .RequireAuthorization(leer)
        .WithName("ListarDim3")
        .WithSummary("Dimensiones 3 paginadas con grupo y Dim2 resueltos; filtros por dim2, grupo, estatus y q (CECO-PR4)")
        .Produces<PagedResponse<Dim3ListItem>>(StatusCodes.Status200OK);

        dim3.MapGet("/{id:guid}", async (
            Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var dto = await mediator.Send(new ObtenerDim3PorIdQuery(id), ct);
            SetEtag(response, dto.Version);
            return Results.Ok(dto);
        })
        .RequireAuthorization(leer)
        .WithName("ObtenerDim3")
        .WithSummary("Detalle de la Dimensión 3 con grupo y Dim2 resueltos (ETag)")
        .Produces<Dim3DetalleDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        dim3.MapPost("/", async (
            CrearDim3Command command, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            SetEtag(response, result.Version);
            return Results.Created($"/api/v1/centros-costo/dim3/{result.Id}", result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("CrearDim3")
        .WithSummary("Crea una Dimensión 3 bajo una Dimensión 2 activa (padre inmutable post-alta)")
        .Produces<Dim3Response>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        dim3.MapPatch("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            EditarDim3Request body,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(
                new EditarDim3Command(id, version, body.Clave, body.Nombre, body.GrupoDim3Id), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("EditarDim3")
        .WithSummary("Edita clave/nombre/grupo — nunca el padre (If-Match)")
        .Produces<Dim3Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        dim3.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(new CambiarEstatusDim3Command(id, version, Activar: false), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("DesactivarDim3")
        .WithSummary("Baja lógica de la Dimensión 3 (hoja — sin cascada; If-Match)")
        .Produces<Dim3Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        dim3.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(new CambiarEstatusDim3Command(id, version, Activar: true), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar)
        .WithName("ReactivarDim3")
        .WithSummary("Reactiva la Dimensión 3 (Dim2 debe estar activa; If-Match)")
        .Produces<Dim3Response>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return app;
    }

    // ─── Grupos: mapeo genérico (GrupoDim2/GrupoDim3 tienen forma idéntica) ──

    private static void MapGrupoDim<TCrear, TRenombrar, TEstatus, TObtener, TListar>(
        IEndpointRouteBuilder app,
        string ruta,
        string nombreRecurso,
        Func<string, TCrear> crear,
        Func<Guid, int, string, TRenombrar> renombrar,
        Func<Guid, int, bool, TEstatus> cambiarEstatus,
        Func<Guid, TObtener> obtener,
        Func<EstatusCatalogo?, string?, int, int, TListar> listar,
        string policyLeer,
        string policyAdministrar)
        where TCrear : IRequest<GrupoDimResponse>
        where TRenombrar : IRequest<GrupoDimResponse>
        where TEstatus : IRequest<GrupoDimResponse>
        where TObtener : IRequest<GrupoDimResponse>
        where TListar : IRequest<PagedResponse<GrupoDimResponse>>
    {
        var group = app
            .MapGroup($"/api/v1/centros-costo/{ruta}")
            .WithTags("CentrosCosto")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(listar(estatus, q, off, lim), ct));
        })
        .RequireAuthorization(policyLeer)
        .WithName($"Listar{nombreRecurso}s")
        .WithSummary($"{nombreRecurso}s paginados con filtros por estatus y q (CECO-PR4)")
        .Produces<PagedResponse<GrupoDimResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (
            Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var dto = await mediator.Send(obtener(id), ct);
            SetEtag(response, dto.Version);
            return Results.Ok(dto);
        })
        .RequireAuthorization(policyLeer)
        .WithName($"Obtener{nombreRecurso}")
        .WithSummary($"Detalle del {nombreRecurso} (ETag)")
        .Produces<GrupoDimResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            GrupoDimRequest body, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(crear(body.Nombre), ct);
            SetEtag(response, result.Version);
            return Results.Created($"/api/v1/centros-costo/{ruta}/{result.Id}", result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(policyAdministrar)
        .WithName($"Crear{nombreRecurso}")
        .WithSummary($"Crea un {nombreRecurso} (grupo de clasificación — no es nivel)")
        .Produces<GrupoDimResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            GrupoDimRequest body,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(renombrar(id, version, body.Nombre), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(policyAdministrar)
        .WithName($"Renombrar{nombreRecurso}")
        .WithSummary($"Renombra el {nombreRecurso} (If-Match)")
        .Produces<GrupoDimResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(cambiarEstatus(id, version, false), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(policyAdministrar)
        .WithName($"Desactivar{nombreRecurso}")
        .WithSummary($"Desactiva el {nombreRecurso} (sin cascada — solo deja de ofrecerse; If-Match)")
        .Produces<GrupoDimResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!TryParseVersion(ifMatch, out var version)) return IfMatchRequerido();
            var result = await mediator.Send(cambiarEstatus(id, version, true), ct);
            SetEtag(response, result.Version);
            return Results.Ok(result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(policyAdministrar)
        .WithName($"Reactivar{nombreRecurso}")
        .WithSummary($"Reactiva el {nombreRecurso} (If-Match)")
        .Produces<GrupoDimResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);
    }

    // ─── Paginación (molde local de AlmacenCatalogoEndpoints) ────────────────

    internal static (int Offset, int Limit) NormalizePaging(int? offset, int? limit) =>
        (Math.Max(0, offset ?? 0), Math.Clamp(limit ?? 50, 1, 200));

    // ─── Helpers If-Match (molde local de CajasEndpoints — ADR-0012) ─────────

    internal static void SetEtag(HttpResponse response, int version) =>
        response.Headers.ETag = $"\"{version.ToString(CultureInfo.InvariantCulture)}\"";

    internal static IResult IfMatchRequerido() => Results.Problem(
        title: "If-Match requerido",
        detail: "La mutación requiere el header If-Match con la versión actual del recurso (ETag, ADR-0012).",
        statusCode: StatusCodes.Status428PreconditionRequired);

    internal static bool TryParseVersion(string? ifMatch, out int version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(ifMatch)) return false;
        var s = ifMatch.Trim();
        if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        s = s.Trim('"');
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
    }

    // ─── Bodies (la versión viaja en el header If-Match, no en el body) ──────

    public sealed record EditarDim1Request(string Clave, string Nombre);
    public sealed record EditarDim2Request(string Clave, string Nombre, Guid GrupoDim2Id);
    public sealed record EditarDim3Request(string Clave, string Nombre, Guid GrupoDim3Id);
    public sealed record GrupoDimRequest(string Nombre);
}

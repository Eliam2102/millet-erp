using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// Dimensiones contables (F1-CON-02) bajo <c>/api/v1/contabilidad/*</c>: tipos de documento, reglas cuenta × tipo × dimensión
/// con vigencia, sucursales de cada centro de costo, validación de movimientos y movimientos de prueba. Mismas convenciones
/// que el catálogo: ETag en GET por id, <c>If-Match</c> en mutaciones de recursos versionados, <c>Idempotency-Key</c> en POST/PUT.
/// </summary>
public static class ContabilidadDimensionesEndpoints
{
    public sealed record EditarTipoDocumentoRequest(string Nombre, bool Activo);

    public sealed record EditarReglaRequest(RequerimientoDimension Requerimiento, DateOnly VigenteDesde, DateOnly? VigenteHasta, string? Nota);

    public sealed record CerrarReglaRequest(DateOnly VigenteHasta);

    public sealed record AsignarSucursalesRequest(IReadOnlyList<Guid> SucursalIds);

    public static IEndpointRouteBuilder MapContabilidadDimensionesEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadDimensionesLeer;
        var administrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadDimensionesAdministrar;
        var validar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadMovimientosValidar;

        var g = app.MapGroup("/api/v1/contabilidad").WithTags("Contabilidad · Dimensiones").RequireAuthorization();

        // ─── Tipos de documento ──────────────────────────────────────────────

        g.MapGet("/tipos-documento", async ([FromQuery] bool? incluirInactivos, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarTiposDocumentoQuery(incluirInactivos ?? false), ct)))
        .RequireAuthorization(leer).WithName("ListarTiposDocumentoContable")
        .Produces<IReadOnlyList<TipoDocumentoResponse>>();

        g.MapPost("/tipos-documento", async (CrearTipoDocumentoCommand command, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(command, ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Created($"/api/v1/contabilidad/tipos-documento/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("CrearTipoDocumentoContable")
        .Produces<TipoDocumentoResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);

        g.MapPut("/tipos-documento/{id:guid}", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, EditarTipoDocumentoRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new EditarTipoDocumentoCommand(id, version, b.Nombre, b.Activo), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("EditarTipoDocumentoContable")
        .Produces<TipoDocumentoResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // ─── Reglas de dimensión ─────────────────────────────────────────────

        g.MapGet("/reglas-dimension", async (
            [FromQuery] Guid? cuentaId, [FromQuery] Guid? tipoDocumentoId, [FromQuery] DimensionContable? dimension,
            [FromQuery] DateOnly? vigentesA, [FromQuery] bool? esPrueba, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = Helpers.NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(new ListarReglasQuery(cuentaId, tipoDocumentoId, dimension, vigentesA, esPrueba, off, lim), ct));
        })
        .RequireAuthorization(leer).WithName("ListarReglasDimension")
        .Produces<PagedResponse<ReglaDimensionResponse>>();

        g.MapGet("/reglas-dimension/efectivas", async (
            [FromQuery] Guid cuentaId, [FromQuery] Guid? tipoDocumentoId, [FromQuery] DateOnly? fecha, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new MatrizEfectivaQuery(cuentaId, tipoDocumentoId, fecha), ct)))
        .RequireAuthorization(leer).WithName("MatrizEfectivaDimensiones")
        .WithSummary("Requerimiento efectivo de cada dimensión para una cuenta y tipo de documento a una fecha (hoy por defecto)")
        .Produces<IReadOnlyList<RequerimientoEfectivo>>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapGet("/reglas-dimension/{id:guid}", async (Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ObtenerReglaQuery(id), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer).WithName("ObtenerReglaDimension")
        .Produces<ReglaDimensionResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/reglas-dimension", async (CrearReglaCommand command, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(command, ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Created($"/api/v1/contabilidad/reglas-dimension/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("CrearReglaDimension")
        .Produces<ReglaDimensionResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPut("/reglas-dimension/{id:guid}", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, EditarReglaRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new EditarReglaCommand(id, version, b.Requerimiento, b.VigenteDesde, b.VigenteHasta, b.Nota), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("EditarReglaDimension")
        .WithSummary("Solo reglas que aún no inician; una regla en vigor se cierra y se crea otra")
        .Produces<ReglaDimensionResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapPost("/reglas-dimension/{id:guid}/cerrar", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, CerrarReglaRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new CerrarReglaCommand(id, version, b.VigenteHasta), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("CerrarReglaDimension")
        .Produces<ReglaDimensionResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // ─── Centros de costo (Dim2) ↔ sucursal ──────────────────────────────

        g.MapGet("/sucursales", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarSucursalesContablesQuery(), ct)))
        .RequireAuthorization(leer).WithName("ListarSucursalesContables")
        .WithSummary("Sucursales de la empresa para asignarlas a los centros de costo")
        .Produces<IReadOnlyList<SucursalContable>>();

        g.MapGet("/centros-sucursal", async (
            [FromQuery] string? q, [FromQuery] Guid? sucursalId, [FromQuery] bool? soloSinSucursal, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarCentrosSucursalQuery(q, sucursalId, soloSinSucursal ?? false, Math.Clamp(limit ?? 100, 1, 500)), ct)))
        .RequireAuthorization(leer).WithName("ListarCentrosSucursal")
        .Produces<IReadOnlyList<CentroSucursalesResponse>>();

        g.MapPut("/centros-sucursal/{dim2Id:guid}", async (Guid dim2Id, AsignarSucursalesRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AsignarSucursalesCentroCommand(dim2Id, b.SucursalIds ?? []), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("AsignarSucursalesCentro")
        .WithSummary("Reemplaza las sucursales en las que se puede usar el centro de costo (Dimensión 2)")
        .Produces<CentroSucursalesResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ─── Captura y validación de movimientos ─────────────────────────────

        g.MapGet("/movimientos/sucursales", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new SucursalesDisponiblesQuery(), ct)))
        .RequireAuthorization(validar).WithName("SucursalesParaMovimiento")
        .WithSummary("Sucursales que el usuario puede operar (todas con el permiso de bypass)")
        .Produces<IReadOnlyList<SucursalContable>>();

        g.MapGet("/movimientos/centros", async (
            [FromQuery] Guid sucursalId, [FromQuery] DimensionContable? nivel, [FromQuery] Guid? dim2Id, [FromQuery] string? q, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CentrosParaMovimientoQuery(sucursalId, nivel ?? DimensionContable.Dim3, dim2Id, q, Math.Clamp(limit ?? 50, 1, 200)), ct)))
        .RequireAuthorization(validar).WithName("CentrosParaMovimiento")
        .WithSummary("Centros activos seleccionables en la sucursal (un centro de otra sucursal no aparece)")
        .Produces<IReadOnlyList<CentroOpcionDto>>().ProducesProblem(StatusCodes.Status403Forbidden);

        g.MapPost("/movimientos/validar", async (MovimientoRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ValidarMovimientoDimensionesQuery(b), ct)))
        .RequireAuthorization(validar).WithName("ValidarMovimientoDimensiones")
        .WithSummary("Valida cuenta, tipo, sucursal, centros y reglas vigentes; no guarda nada")
        .Produces<ValidacionDimensiones>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status403Forbidden);

        // ─── Movimientos de prueba (D7) ──────────────────────────────────────

        g.MapPost("/movimientos-prueba", async (MovimientoRequest b, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ConfirmarMovimientoPruebaCommand(b), ct);
            if (!r.Confirmado)
                return Results.Problem(
                    title: "El movimiento no cumple las reglas de dimensión.",
                    detail: string.Join(" ", r.Validacion.Errores.Select(e => e.Mensaje)),
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    type: "https://millet-erp/errors/contab_dim_movimiento_invalido",
                    extensions: new Dictionary<string, object?> { ["code"] = "CONTAB_DIM_MOVIMIENTO_INVALIDO", ["errores"] = r.Validacion.Errores });
            return Results.Created($"/api/v1/contabilidad/movimientos-prueba/{r.Movimiento!.Id}", r.Movimiento);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(validar).WithName("ConfirmarMovimientoPrueba")
        .WithSummary("Valida y guarda un movimiento de PRUEBA con las reglas que lo validaron (conservación histórica)")
        .Produces<MovimientoPruebaResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapGet("/movimientos-prueba", async (
            [FromQuery] Guid? sucursalId, [FromQuery] Guid? cuentaId, [FromQuery] int? offset, [FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = Helpers.NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(new ListarMovimientosPruebaQuery(sucursalId, cuentaId, off, lim), ct));
        })
        .RequireAuthorization(leer).WithName("ListarMovimientosPrueba")
        .Produces<PagedResponse<MovimientoPruebaResponse>>();

        g.MapGet("/movimientos-prueba/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerMovimientoPruebaQuery(id), ct)))
        .RequireAuthorization(leer).WithName("ObtenerMovimientoPrueba")
        .Produces<MovimientoPruebaResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

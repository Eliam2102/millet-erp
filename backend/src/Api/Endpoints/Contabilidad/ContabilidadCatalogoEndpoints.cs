using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Catalogo;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// Catálogo contable: cuentas, importación (vista previa / perfilado / aplicar) y configuración de
/// formato, bajo <c>/api/v1/contabilidad/*</c> (F1-CON-01). Convenciones del repo: ETag en GET por id,
/// <c>If-Match</c> obligatorio en mutaciones (428/409), <c>Idempotency-Key</c> en mutaciones (ADR-0020).
/// Los helpers de paginación/ETag se reutilizan de Centros de Costo (mismo assembly, sin duplicar).
/// </summary>
public static class ContabilidadCatalogoEndpoints
{
    public sealed record EditarCuentaRequest(
        string Nombre, Guid? PadreId, NaturalezaCuenta? Naturaleza, TipoCuenta? Tipo, CuentaControl CuentaControl,
        string? CodigoAgrupador, string? GrupoReporte, Guid? RubroId = null, bool NoAfectableManual = false);

    public sealed record ResolverSolicitudRequest(bool Autorizar, string? Motivo);

    public sealed record ValidarMovimientoRequest(string? Codigo, Guid? CuentaId, OrigenMovimiento Origen);

    public static IEndpointRouteBuilder MapContabilidadEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadCatalogoLeer;
        var administrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadCatalogoAdministrar;
        var importar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadCatalogoImportar;

        var g = app.MapGroup("/api/v1/contabilidad").WithTags("Contabilidad").RequireAuthorization();

        var autorizar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadCatalogoAutorizar;
        g.MapGet("/solicitudes", async ([FromQuery] string? estado, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = Helpers.NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(new ListarSolicitudesCatalogoQuery(estado, off, lim), ct));
        }).RequireAuthorization(leer).WithName("ListarSolicitudesCatalogo");
        g.MapGet("/solicitudes/{id:guid}", async (Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var solicitud = await mediator.Send(new ObtenerSolicitudCatalogoQuery(id), ct);
            Helpers.SetEtag(response, solicitud.Version);
            return Results.Ok(solicitud);
        }).RequireAuthorization(leer).WithName("ObtenerSolicitudCatalogo");
        g.MapPost("/solicitudes/{id:guid}/resolver", async (Guid id, ResolverSolicitudRequest b,
            [FromHeader(Name = "If-Match")] string? ifMatch, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            return Results.Ok(await mediator.Send(new ResolverSolicitudCatalogoCommand(id, version, b.Autorizar, b.Motivo), ct));
        }).RequireAuthorization(autorizar).WithMetadata(new RequireIdempotencyKeyAttribute())
            .WithName("ResolverSolicitudCatalogo");

        // ─── Cuentas ─────────────────────────────────────────────────────────

        g.MapGet("/cuentas", async (
            [FromQuery] EstatusCatalogo? estatus, [FromQuery] TipoCuenta? tipo, [FromQuery] string? q,
            [FromQuery] Guid? padreId, [FromQuery] bool? pendientes, [FromQuery] int? offset, [FromQuery] int? limit,
            [FromQuery] ClaseCuenta? clase, [FromQuery] Guid? rubroId, IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = Helpers.NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(new ListarCuentasQuery(estatus, tipo, q, padreId, pendientes, off, lim, clase, rubroId), ct));
        })
        .RequireAuthorization(leer).WithName("ListarCuentasContables")
        .Produces<PagedResponse<CuentaResponse>>();

        g.MapGet("/cuentas/arbol", async (
            [FromQuery] EstatusCatalogo? estatus, [FromQuery] Guid? raizId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ArbolCuentasQuery(estatus, raizId), ct)))
        .RequireAuthorization(leer).WithName("ArbolCuentasContables")
        .WithSummary("Hijas directas de raizId (null = raíces); carga perezosa");

        g.MapGet("/cuentas/siguiente-codigo", async ([FromQuery] Guid padreId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new SiguienteCodigoQuery(padreId), ct)))
        .RequireAuthorization(administrar).WithName("SiguienteCodigoCuentaContable")
        .WithSummary("Código sugerido (editable) para una hija nueva del padre; null + motivo si no se puede inferir")
        .Produces<SiguienteCodigoResponse>().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapGet("/cuentas/{id:guid}", async (Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var dto = await mediator.Send(new ObtenerCuentaQuery(id), ct);
            Helpers.SetEtag(response, dto.Version);
            return Results.Ok(dto);
        })
        .RequireAuthorization(leer).WithName("ObtenerCuentaContable")
        .Produces<CuentaResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/cuentas", async (CrearCuentaCommand command, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(command, ct);
            Helpers.SetEtag(response, result.Version);
            return Results.Accepted($"/api/v1/contabilidad/solicitudes/{result.SolicitudId}", result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("CrearCuentaContable")
        .Produces<CuentaResponse>(StatusCodes.Status202Accepted)
        .ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPut("/cuentas/{id:guid}", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, EditarCuentaRequest b,
            HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var result = await mediator.Send(new EditarCuentaCommand(
                id, version, b.Nombre, b.PadreId, b.Naturaleza, b.Tipo, b.CuentaControl, b.CodigoAgrupador, b.GrupoReporte, b.RubroId, b.NoAfectableManual), ct);
            Helpers.SetEtag(response, result.Version);
            return Results.Accepted($"/api/v1/contabilidad/solicitudes/{result.SolicitudId}", result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("EditarCuentaContable")
        .Produces<CuentaResponse>().ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapPost("/cuentas/{id:guid}/desactivar", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var result = await mediator.Send(new DesactivarCuentaCommand(id, version), ct);
            Helpers.SetEtag(response, result.Version);
            return Results.Accepted($"/api/v1/contabilidad/solicitudes/{result.SolicitudId}", result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("DesactivarCuentaContable");

        g.MapPost("/cuentas/{id:guid}/reactivar", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var result = await mediator.Send(new ReactivarCuentaCommand(id, version), ct);
            Helpers.SetEtag(response, result.Version);
            return Results.Accepted($"/api/v1/contabilidad/solicitudes/{result.SolicitudId}", result);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(administrar).WithName("ReactivarCuentaContable");

        g.MapPost("/cuentas/validar-movimiento", async (ValidarMovimientoRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ValidarMovimientoQuery(b.Codigo, b.CuentaId, b.Origen), ct)))
        .RequireAuthorization(leer).WithName("ValidarMovimientoCuentaContable")
        .Produces<CuentaContableValidacion>();

        g.MapGet("/configuracion-formato", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConfiguracionFormatoQuery(), ct)))
        .RequireAuthorization(leer).WithName("ConfiguracionFormatoCatalogo");

        // ─── Importación ─────────────────────────────────────────────────────

        g.MapPost("/importaciones/vista-previa", async (ImportacionRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new VistaPreviaImportacionCommand(b), ct)))
        .RequireAuthorization(importar).WithName("VistaPreviaImportacionCatalogo")
        .Produces<VistaPreviaResponse>().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPost("/importaciones/perfilado", async (ImportacionRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new PerfilarImportacionCommand(b), ct)))
        .RequireAuthorization(importar).WithName("PerfiladoImportacionCatalogo")
        .WithSummary("Reporte de perfilado de solo lectura (cero escrituras)")
        .Produces<PerfilImportacion>().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPost("/importaciones", async (ImportacionRequest b, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new AplicarImportacionCommand(b), ct);
            if (r.Errores.Count > 0)
                return Results.Problem(
                    title: "El archivo tiene filas con errores; no se escribió nada.",
                    detail: $"{r.Errores.Count} errores. Corrija el archivo y vuelva a la vista previa.",
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    type: "https://millet-erp/errors/contab_import_filas_con_errores",
                    extensions: new Dictionary<string, object?> { ["code"] = "CONTAB_IMPORT_FILAS_CON_ERRORES", ["errores"] = r.Errores });
            var cuerpo = new { idempotente = r.Idempotente, lote = r.Lote, solicitudId = r.SolicitudId };
            if (r.SolicitudId is not null) return Results.Accepted($"/api/v1/contabilidad/solicitudes/{r.SolicitudId}", cuerpo);
            return r.Aplicado ? Results.Created($"/api/v1/contabilidad/importaciones/{r.Lote!.Id}", cuerpo) : Results.Ok(cuerpo);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(importar).WithName("AplicarImportacionCatalogo")
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapGet("/importaciones", async ([FromQuery] int? offset, [FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
        {
            var (off, lim) = Helpers.NormalizePaging(offset, limit);
            return Results.Ok(await mediator.Send(new ListarLotesQuery(off, lim), ct));
        })
        .RequireAuthorization(leer).WithName("ListarLotesImportacionCatalogo");

        return app;
    }
}

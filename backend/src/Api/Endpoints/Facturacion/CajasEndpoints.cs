using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Cajas;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Domain.Cajas;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de administración de Cajas (CAJAS-PR1, 12-cajas.md §10).
/// <c>/api/v1/facturacion/cajas</c>: CRUD + replace-set de alcances
/// (sucursales/canales/usuarios) + alcances administrativos por usuario.
/// Las sesiones de efectivo y los cobros de mostrador entran en
/// CAJAS-PR3/PR4.
/// </summary>
public static class CajasEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionCajasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/cajas")
            .WithTags("Facturacion");

        // GET: bandeja de cajas. CAJAS-PR6: administrar ∨ operar ∨ supervisar
        // (OR resuelto en el handler; alimenta el selector de apertura).
        group.MapGet("/", async (
            [FromQuery] bool? soloActivas,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(new ListarCajasQuery(soloActivas ?? false), cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization()
        .WithName("ListarCajas")
        .WithSummary("Bandeja de cajas del módulo Facturación")
        .Produces<IReadOnlyList<CajaListadoItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET: alcances administrativos (usuarios sin caja, [Decisión 12-6]).
        // Antes de /{id:guid} sólo por legibilidad; las rutas no colisionan.
        group.MapGet("/usuario-alcances", async (
            [FromQuery] Guid? usuarioId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(new ListarUsuarioAlcancesQuery(usuarioId), cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar)
        .WithName("ListarUsuarioAlcances")
        .WithSummary("Concesiones de alcance de usuarios sin caja")
        .Produces<IReadOnlyList<UsuarioAlcanceItem>>(StatusCodes.Status200OK);

        // PUT: replace-set de concesiones de un usuario.
        group.MapPut("/usuario-alcances/{usuarioId:guid}", async (
            Guid usuarioId,
            ReemplazarUsuarioAlcancesRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ReemplazarUsuarioAlcancesCommand(usuarioId, body.Alcances), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar)
        .WithName("ReemplazarUsuarioAlcances")
        .WithSummary("Reemplaza las concesiones de alcance de un usuario sin caja")
        .Produces<UsuarioAlcancesResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        // GET: detalle con alcances + versión (ETag para If-Match).
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(new CajaDetalleQuery(id), cancellationToken);
            response.Headers.ETag = $"\"{detalle.Version.ToString(CultureInfo.InvariantCulture)}\"";
            return Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar)
        .WithName("CajaDetalle")
        .WithSummary("Detalle de una caja (alcances + versión)")
        .Produces<CajaDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // GET: ajustes de la caja — pendientes de drenar en la próxima
        // apertura (y aplicados con ?incluirAplicados=true) ([12-C], PR7).
        // OR administrar∨operar∨supervisar resuelto en el handler.
        group.MapGet("/{id:guid}/ajustes", async (
            Guid id,
            [FromQuery] bool? incluirAplicados,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(
                new ListarAjustesCajaQuery(id, incluirAplicados ?? false), cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization()
        .WithName("ListarAjustesCaja")
        .WithSummary("Ajustes pendientes (y opcionalmente aplicados) de una caja")
        .Produces<IReadOnlyList<CajaAjusteItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // POST: alta de caja.
        group.MapPost("/", async (
            CrearCajaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/cajas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar)
        .WithName("CrearCaja")
        .WithSummary("Da de alta una caja (nace activa y sin alcance)")
        .Produces<CajaMutadaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // PUT: edición de datos generales + estatus (If-Match).
        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            ActualizarCajaRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseVersion(ifMatch, out var version))
                return IfMatchRequerido();

            var response = await mediator.Send(
                new ActualizarCajaCommand(id, version, body.Nombre, body.Descripcion, body.Activa),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar)
        .WithName("ActualizarCaja")
        .WithSummary("Edita nombre/descripción/estatus de una caja (If-Match → 409 al choque)")
        .Produces<CajaMutadaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // PUTs replace-set de los tres alcances (If-Match).
        group.MapPut("/{id:guid}/sucursales", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            ReemplazarSucursalesRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseVersion(ifMatch, out var version))
                return IfMatchRequerido();
            var response = await mediator.Send(
                new ReemplazarSucursalesCajaCommand(id, version, body.SucursalIds), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar)
        .WithName("ReemplazarSucursalesCaja")
        .WithSummary("Reemplaza las sucursales del alcance de la caja")
        .Produces<CajaMutadaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPut("/{id:guid}/canales", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            ReemplazarCanalesRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseVersion(ifMatch, out var version))
                return IfMatchRequerido();
            var response = await mediator.Send(
                new ReemplazarCanalesCajaCommand(id, version, body.CanalVentaIds), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar)
        .WithName("ReemplazarCanalesCaja")
        .WithSummary("Reemplaza los canales de venta del alcance de la caja")
        .Produces<CajaMutadaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPut("/{id:guid}/usuarios", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            ReemplazarUsuariosRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseVersion(ifMatch, out var version))
                return IfMatchRequerido();
            var response = await mediator.Send(
                new ReemplazarUsuariosCajaCommand(id, version, body.UsuarioIds), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar)
        .WithName("ReemplazarUsuariosCaja")
        .WithSummary("Reemplaza los cajeros relacionados a la caja")
        .Produces<CajaMutadaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        MapSesiones(group);

        return app;
    }

    /// <summary>Endpoints de la Capa B — sesiones de efectivo (CAJAS-PR3, 12-cajas.md §10).</summary>
    private static void MapSesiones(RouteGroupBuilder group)
    {
        var operar = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaOperar;
        var supervisar = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaSupervisar;
        var liquidar = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaLiquidar;

        // POST: autorización consumible de apertura de caja ajena ([Decisión 12-1]).
        group.MapPost("/{id:guid}/autorizaciones-apertura", async (
            Guid id,
            CrearAutorizacionAperturaRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new CrearAutorizacionAperturaCommand(id, body.CajeroUsuarioId, body.Motivo), cancellationToken);
            return Results.Created($"/api/v1/facturacion/cajas/{id}/autorizaciones-apertura/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(supervisar)
        .WithName("CrearAutorizacionAperturaCaja")
        .WithSummary("Autoriza a un cajero a abrir una caja ajena (consumible, vigencia corta)")
        .Produces<AutorizacionAperturaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: sesión vigente del cajero (panel "Mi caja").
        group.MapGet("/sesion-actual", async (IMediator mediator, CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new SesionActualQuery(), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(operar)
        .WithName("CajaSesionActual")
        .WithSummary("Sesión de caja vigente del usuario actual (null si no tiene)")
        .Produces<SesionActualResponse>(StatusCodes.Status200OK);

        // POST: apertura de sesión (§5.1).
        group.MapPost("/{id:guid}/sesiones", async (
            Guid id,
            AbrirCajaSesionRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new AbrirCajaSesionCommand(id, body.SucursalId, body.FondoApertura, body.AutorizacionAperturaId),
                cancellationToken);
            return Results.Created($"/api/v1/facturacion/cajas/sesiones/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(operar)
        .WithName("AbrirCajaSesion")
        .WithSummary("Abre la sesión de efectivo (fondo + sucursal de operación)")
        .Produces<CajaSesionMutadaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: sesiones de una caja (supervisar ∨ administrar — OR en el handler).
        group.MapGet("/{id:guid}/sesiones", async (
            Guid id,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(
                new ListarCajaSesionesQuery(id, offset ?? 0, limit ?? 50), cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization()
        .WithName("ListarCajaSesiones")
        .WithSummary("Historial de sesiones de una caja")
        .Produces<IReadOnlyList<CajaSesionListadoItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // GET: detalle de una sesión con movimientos + cortes (ETag).
        group.MapGet("/sesiones/{id:guid}", async (
            Guid id,
            IMediator mediator,
            HttpResponse response,
            CancellationToken cancellationToken) =>
        {
            var detalle = await mediator.Send(new CajaSesionDetalleQuery(id), cancellationToken);
            response.Headers.ETag = $"\"{detalle.Version.ToString(CultureInfo.InvariantCulture)}\"";
            return Results.Ok(detalle);
        })
        .RequireAuthorization()
        .WithName("CajaSesionDetalle")
        .WithSummary("Detalle de una sesión (movimientos + cortes + versión)")
        .Produces<CajaSesionDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST: movimiento manual (depósito / retiro).
        group.MapPost("/sesiones/{id:guid}/movimientos", async (
            Guid id,
            RegistrarCajaMovimientoRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new RegistrarCajaMovimientoCommand(
                    id, body.Tipo, body.FormaPago, body.Importe, body.Descripcion, body.Referencia),
                cancellationToken);
            return Results.Created($"/api/v1/facturacion/cajas/sesiones/{id}/movimientos/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(operar)
        .WithName("RegistrarCajaMovimiento")
        .WithSummary("Registra un depósito o retiro manual en la sesión")
        .Produces<CajaMovimientoRegistradoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST: iniciar arqueo (calcula esperado por forma; If-Match).
        group.MapPost("/sesiones/{id:guid}/arqueo", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseVersion(ifMatch, out var version))
                return IfMatchRequerido();
            var response = await mediator.Send(new IniciarArqueoCajaSesionCommand(id, version), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(operar)
        .WithName("IniciarArqueoCajaSesion")
        .WithSummary("Pasa la sesión a EnArqueo con el esperado por forma de pago")
        .Produces<CajaSesionMutadaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // POST: cierre / liquidación (permiso caja.liquidar, [Decisión 12-11]).
        group.MapPost("/sesiones/{id:guid}/cierre", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            CerrarCajaSesionRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseVersion(ifMatch, out var version))
                return IfMatchRequerido();
            var response = await mediator.Send(
                new CerrarCajaSesionCommand(id, version, body.EfectivoDeclarado, body.NotasCierre),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(liquidar)
        .WithName("CerrarCajaSesion")
        .WithSummary("Cierra la sesión con el contado físico (arqueo, inmutable)")
        .Produces<CajaSesionMutadaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // POST: reabrir (EnArqueo → Abierta, supervisor).
        group.MapPost("/sesiones/{id:guid}/reabrir", async (
            Guid id,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (!TryParseVersion(ifMatch, out var version))
                return IfMatchRequerido();
            var response = await mediator.Send(new ReabrirCajaSesionCommand(id, version), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(supervisar)
        .WithName("ReabrirCajaSesion")
        .WithSummary("Devuelve una sesión EnArqueo a Abierta (falta registrar algo)")
        .Produces<CajaSesionMutadaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);
    }

    private static IResult IfMatchRequerido() => Results.Problem(
        title: "If-Match requerido",
        detail: "La mutación requiere el header If-Match con la versión actual de la caja (ETag, ADR-0012).",
        statusCode: StatusCodes.Status428PreconditionRequired);

    private static bool TryParseVersion(string? ifMatch, out int version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(ifMatch)) return false;
        var s = ifMatch.Trim();
        if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        s = s.Trim('"');
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out version);
    }

    /// <summary>Body de edición (la versión va en el header If-Match).</summary>
    public sealed record ActualizarCajaRequest(string Nombre, string? Descripcion, bool Activa);

    public sealed record ReemplazarSucursalesRequest(IReadOnlyList<Guid> SucursalIds);
    public sealed record ReemplazarCanalesRequest(IReadOnlyList<short> CanalVentaIds);
    public sealed record ReemplazarUsuariosRequest(IReadOnlyList<Guid> UsuarioIds);
    public sealed record ReemplazarUsuarioAlcancesRequest(IReadOnlyList<UsuarioAlcanceInput> Alcances);

    // ---- Bodies de la Capa B (CAJAS-PR3) ----
    public sealed record CrearAutorizacionAperturaRequest(Guid CajeroUsuarioId, string Motivo);
    public sealed record AbrirCajaSesionRequest(Guid SucursalId, decimal FondoApertura, Guid? AutorizacionAperturaId = null);
    public sealed record RegistrarCajaMovimientoRequest(
        TipoCajaMovimiento Tipo, string FormaPago, decimal Importe, string Descripcion, string? Referencia = null);
    public sealed record CerrarCajaSesionRequest(decimal EfectivoDeclarado, string? NotasCierre = null);
}

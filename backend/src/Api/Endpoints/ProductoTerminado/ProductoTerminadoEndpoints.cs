using System.Globalization;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Application.Consultas;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.ProductoTerminado.Application.Escaneos;
using Millet.ProductoTerminado.Application.Liberacion;

namespace Millet.Api.Endpoints.ProductoTerminado;

public static class ProductoTerminadoEndpoints
{
    public static void MapProductoTerminado(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/producto-terminado").WithTags("Producto terminado").RequireAuthorization();

        // El alcance por sucursal de los listados lo aplican los handlers (PtAcceso).
        g.MapGet("/piezas", async ([FromQuery] string? pedido, [FromQuery] Guid? sucursalId, [FromQuery] int? page, [FromQuery] int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarPiezasQuery(pedido, sucursalId, page ?? 1, pageSize ?? 50), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);

        g.MapGet("/salidas-pendientes", async ([FromQuery] Guid? sucursalId, [FromQuery] string? alerta, [FromQuery] string? buscar, [FromQuery] int? page, [FromQuery] int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarSalidasPendientesQuery(sucursalId, alerta, buscar, page ?? 1, pageSize ?? 50), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);

        g.MapGet("/escaneos", async ([FromQuery] string? resultado, [FromQuery] int? page, [FromQuery] int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarEscaneosQuery(resultado, page ?? 1, pageSize ?? 50), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar);

        var escaneo = g.MapGroup("/escaneos/{id:guid}")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar);
        escaneo.AddEndpointFilter(async (context, next) =>
        {
            if (Guid.TryParse(context.HttpContext.Request.RouteValues["id"]?.ToString(), out var id))
            {
                var scope = context.HttpContext.RequestServices.GetRequiredService<DocumentoSucursalScope>();
                await scope.VerificarAsync("escaneo_pt", id, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, context.HttpContext.RequestAborted);
            }
            return await next(context);
        });
        escaneo.MapPost("/reintentar", async (Guid id, HttpRequest req, HttpResponse res, IMediator mediator, CancellationToken ct) =>
            Version(req) is int v ? Respuesta(await mediator.Send(new ReintentarEscaneoCommand(id, v), ct), res) : SinVersion())
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        escaneo.MapPost("/descartar", async (Guid id, DescartarBody body, HttpRequest req, HttpResponse res, IMediator mediator, CancellationToken ct) =>
            Version(req) is int v ? Respuesta(await mediator.Send(new DescartarEscaneoCommand(id, v, body.Motivo), ct), res) : SinVersion())
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        escaneo.MapPost("/revisar", async (Guid id, HttpRequest req, HttpResponse res, IMediator mediator, CancellationToken ct) =>
            Version(req) is int v ? Respuesta(await mediator.Send(new MarcarReposicionRevisadaCommand(id, v), ct), res) : SinVersion())
            .WithMetadata(new RequireIdempotencyKeyAttribute());

        // Solo Demo (PT5.2): el saldo de prueba simula el cobro de caja. Nunca se mapea en Production; además exige el origen Demo de A+W.
        // PLATFORM-TODO(<SaldoPorPedidoFacturacion>): se retira cuando Facturación (Uziel) publique el saldo por pedido.
        if (!app.ServiceProvider.GetRequiredService<IHostEnvironment>().IsProduction())
            g.MapPut("/demo/pedidos/{numero}/saldo", async (string numero, SaldoDemoBody body, AwOrigenSesion sesion, IMediator mediator, CancellationToken ct) =>
                await sesion.EsDemoAsync(ct)
                    ? Results.Ok(new { liberado = await mediator.Send(new PonerSaldoDemoCommand(numero, body.Saldo), ct) })
                    : Results.Problem(statusCode: 409, title: "El saldo de prueba solo existe en el origen Demo.", extensions: new Dictionary<string, object?> { ["code"] = "PT_SOLO_DEMO" }))
                .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar);
    }

    private static int? Version(HttpRequest req)
    {
        var raw = req.Headers.IfMatch.ToString();
        return raw.Length >= 3 && raw[0] == '"' && raw[^1] == '"' && int.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v >= 1 ? v : null;
    }

    private static IResult SinVersion() => Results.Problem(statusCode: 428, title: "Envíe If-Match con la versión vigente del escaneo.", extensions: new Dictionary<string, object?> { ["code"] = "IF_MATCH_REQUIRED" });

    private static IResult Respuesta(EscaneoDto d, HttpResponse response) { response.Headers.ETag = $"\"{d.Version.ToString(CultureInfo.InvariantCulture)}\""; return Results.Ok(d); }

    public sealed record DescartarBody(string Motivo);

    public sealed record SaldoDemoBody(decimal Saldo);
}

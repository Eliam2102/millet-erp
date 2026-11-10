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
using Millet.ProductoTerminado.Application.Liquidacion;
using Millet.ProductoTerminado.Application.Entregas;
using Millet.ProductoTerminado.Application.Salidas;
using Millet.ProductoTerminado.Application.Traspasos;

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

        // Traspasos entre sucursales (PT5.9). El alcance por sucursal lo aplican los handlers (PtAcceso).
        var traspasos = g.MapGroup("/traspasos");
        traspasos.MapGet("", async ([FromQuery] string? estado, [FromQuery] Guid? sucursalId, [FromQuery] int? page, [FromQuery] int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarTraspasosQuery(estado, sucursalId, page ?? 1, pageSize ?? 50), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);
        traspasos.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerTraspasoQuery(id), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);
        traspasos.MapPost("", async (CrearTraspasoCommand cmd, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(cmd, ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        traspasos.MapPost("/{id:guid}/recibir", async (Guid id, RecibirBody? body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RecibirTraspasoCommand(id, body?.PiezaIds), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());

        // Hoja de salida y albaranes (PT5.5). El alcance por sucursal lo aplican los handlers (PtAcceso).
        var hojas = g.MapGroup("/hojas-salida");
        hojas.MapGet("", async ([FromQuery] string? estado, [FromQuery] Guid? sucursalId, [FromQuery] int? page, [FromQuery] int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarHojasSalidaQuery(estado, sucursalId, page ?? 1, pageSize ?? 50), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);
        hojas.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerHojaSalidaQuery(id), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);
        hojas.MapPost("", async (CrearHojaSalidaCommand cmd, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(cmd, ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        hojas.MapPost("/{id:guid}/emitir", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new EmitirHojaSalidaCommand(id), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        // Reglas de salida 2 y 4: el permiso depende del nivel (producto_terminado.salida.aprobar-*); lo exige el handler.
        hojas.MapPost("/{id:guid}/autorizar", async (Guid id, AutorizarHojaBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AutorizarHojaSalidaCommand(id, body.Nivel, body.Motivo, body.Suplencia), ct)))
            .RequireAuthorization()
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        hojas.MapPost("/{id:guid}/cancelar", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CancelarHojaSalidaCommand(id), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());

        // Carga física pieza por pieza (PT5.4) sobre la hoja en Borrador.
        hojas.MapPost("/{id:guid}/cargar", async (Guid id, PiezaHojaBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CargarPiezaHojaCommand(id, body.Etiqueta), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        hojas.MapPost("/{id:guid}/descargar", async (Guid id, PiezaHojaBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DescargarPiezaHojaCommand(id, body.Etiqueta), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        hojas.MapPost("/{id:guid}/rota", async (Guid id, PiezaHojaBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new MarcarPiezaRotaHojaCommand(id, body.Etiqueta), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());

        // Liquidación del chofer en caja (PT5.7) y cierre del día de la contra entrega. El alcance por sucursal lo aplican los handlers (PtAcceso).
        var liquidaciones = g.MapGroup("/liquidaciones");
        liquidaciones.MapGet("", async ([FromQuery] string? estado, [FromQuery] Guid? sucursalId, [FromQuery] int? page, [FromQuery] int? pageSize, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarLiquidacionesQuery(estado, sucursalId, page ?? 1, pageSize ?? 50), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);
        liquidaciones.MapGet("/{salidaId:guid}", async (Guid salidaId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerLiquidacionQuery(salidaId), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);
        liquidaciones.MapPost("/{salidaId:guid}", async (Guid salidaId, LiquidarBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new LiquidarRutaPtCommand(salidaId, body.Albaranes, body.Imprevistos ?? []), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        liquidaciones.MapPost("/cierre-dia", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(new { avisos = await mediator.Send(new CerrarDiaContraEntregaCommand(), ct) }))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        // Entrega con firma y evidencia, parcial y rechazo en sitio (PT5.6): una confirmación por albarán de una hoja emitida.
        hojas.MapPost("/{id:guid}/albaranes/{albaranId:guid}/entrega", async (Guid id, Guid albaranId, EntregaBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConfirmarEntregaCommand(id, albaranId, body.Receptor, body.Rechazadas, body.MotivoRechazo, body.Firma, body.Foto, body.Cobro), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        hojas.MapGet("/{id:guid}/albaranes/{albaranId:guid}/entrega/evidencia", async (Guid id, Guid albaranId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerEvidenciaEntregaQuery(id, albaranId), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);
        hojas.MapPost("/{id:guid}/albaranes/{albaranId:guid}/cancelar", async (Guid id, Guid albaranId, CancelarAlbaranBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CancelarAlbaranSalidaCommand(id, albaranId, body.Motivo), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());

        // Solo Demo (PT5.2): el saldo de prueba simula el cobro de caja. Nunca se mapea en Production; además exige el origen Demo de A+W.
        // El canal Real usa el saldo de Facturación (U-3); este endpoint queda solo para Demo.
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

    /// <summary>Sin cuerpo o sin <c>PiezaIds</c> = recepción total.</summary>
    public sealed record RecibirBody(IReadOnlyList<Guid>? PiezaIds);

    public sealed record LiquidarBody(IReadOnlyList<LiquidacionAlbaranInput> Albaranes, IReadOnlyList<ImprevistoInput>? Imprevistos);

    public sealed record CancelarAlbaranBody(string Motivo);

    public sealed record AutorizarHojaBody(string Nivel, string Motivo, string? Suplencia);

    public sealed record SaldoDemoBody(decimal Saldo);
}

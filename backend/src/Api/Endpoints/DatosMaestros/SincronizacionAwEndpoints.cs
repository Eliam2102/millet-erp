using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Api.Endpoints.DatosMaestros;

/// <summary>Matriz campo → dueño y bitácora de cambios de la sincronización con A+W (ADM-06).</summary>
public sealed record ResolverConflictoBody(string Resolucion, int Version);

public sealed record ReporteColumnaDto(string Clave, string Etiqueta, string Tipo);

/// <summary>Reporte de diferencias de una corrida en el formato JSON de ADR-0036 (el frontend lo exporta).</summary>
public sealed record ReporteDiferenciasAwDto(
    string Titulo, DateTimeOffset GeneradoEn, IReadOnlyDictionary<string, string?> FiltrosAplicados,
    IReadOnlyList<ReporteColumnaDto> Columnas, IReadOnlyList<IReadOnlyDictionary<string, string?>> Filas,
    IReadOnlyDictionary<string, decimal> Totales, bool Truncado);

public static class SincronizacionAwEndpoints
{
    private const int TopeFilasReporte = 10_000;
    private static readonly JsonSerializerOptions JsonResumen = new(JsonSerializerDefaults.Web);

    /// <summary>Lo que se guarda en <c>ResumenProductos</c>: el resumen del barrido o, si el origen no respondió, error + mensaje.</summary>
    private sealed record ResumenProductosJson(
        int Leidos = 0, int Creados = 0, int Actualizados = 0, int SinCambios = 0, int Pendientes = 0, int Conflictos = 0,
        int Errores = 0, IReadOnlyList<AwProductoError>? ErroresPorReferencia = null, string? Error = null, string? Mensaje = null);

    private static readonly ReporteColumnaDto[] ColumnasReporte =
    [
        new("entidad", "Entidad", "texto"), new("referencia", "Referencia A+W", "texto"), new("campo", "Campo", "texto"),
        new("antes", "Antes", "texto"), new("despues", "Después", "texto"), new("dueno", "Dueño", "texto"),
        new("tipo", "Tipo", "texto"), new("estado", "Estado", "texto"), new("causa", "Causa", "texto"),
        new("version", "Versión del evento", "texto"), new("ocurrido", "Fecha", "fecha"),
    ];

    public static IEndpointRouteBuilder MapSincronizacionAwEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/datos-maestros/sincronizacion-aw/matriz",
                () => Results.Ok(new { version = MatrizCampoDuenoAw.Version, campos = MatrizCampoDuenoAw.Campos }))
            .WithTags("DatosMaestros")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesOrigenVer);

        app.MapGet("/api/v1/datos-maestros/clientes/{id:guid}/cambios-aw",
                async (Guid id, int? limit, CompartidoDbContext db, ISender sender, CancellationToken ct) =>
                {
                    if (!await db.Clientes.AsNoTracking().AnyAsync(c => c.Id == id, ct))
                        throw new EntityNotFoundException("CLIENTE_NO_ENCONTRADO", $"No existe cliente con id '{id}'.");
                    return Results.Ok(await sender.Send(new ListarCambiosMaestroAwQuery("Cliente", id, Limit: limit ?? 50), ct));
                })
            .WithTags("DatosMaestros")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesOrigenVer);

        var conflictos = app.MapGroup("/api/v1/datos-maestros/clientes/conflictos-aw").WithTags("DatosMaestros")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesSincronizar);
        conflictos.MapGet("/", async (string? estado, Guid? ejecucionId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListarCambiosMaestroAwQuery("Cliente", Estado: estado ?? "Pendiente",
                EjecucionId: ejecucionId, Limit: 200, SoloConflictos: true), ct)));
        conflictos.MapPost("/{id:guid}/resolver", async (Guid id, ResolverConflictoBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ResolverConflictoMaestroAwCommand(id, "Cliente", body.Resolucion, body.Version), ct);
            return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute());

        var productosPolicy = PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar;
        var conflictosProductos = app.MapGroup("/api/v1/datos-maestros/productos-aw/conflictos-aw").WithTags("DatosMaestros")
            .RequireAuthorization(productosPolicy);
        conflictosProductos.MapGet("/", async (string? estado, Guid? ejecucionId, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ListarCambiosMaestroAwQuery("ProductoAw", Estado: estado ?? "Pendiente",
                EjecucionId: ejecucionId, Limit: 200, SoloConflictos: true), ct)));
        conflictosProductos.MapPost("/{id:guid}/resolver", async (Guid id, ResolverConflictoBody body, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new ResolverConflictoMaestroAwCommand(id, "ProductoAw", body.Resolucion, body.Version), ct);
            return Results.NoContent();
        }).WithMetadata(new RequireIdempotencyKeyAttribute());

        app.MapGet("/api/v1/datos-maestros/productos-aw/{id:guid}/cambios-aw",
                async (Guid id, int? limit, CompartidoDbContext db, ISender sender, CancellationToken ct) =>
                {
                    if (!await db.ProductosAw.AsNoTracking().AnyAsync(p => p.Id == id, ct))
                        throw new EntityNotFoundException("PRODUCTO_AW_NO_ENCONTRADO", $"No existe producto con id '{id}'.");
                    return Results.Ok(await sender.Send(new ListarCambiosMaestroAwQuery("ProductoAw", id, Limit: limit ?? 50), ct));
                })
            .WithTags("DatosMaestros").RequireAuthorization(productosPolicy);

        MapCorridas(app);
        return app;
    }

    private static void MapCorridas(IEndpointRouteBuilder app)
    {
        const string baseCorridas = "/api/v1/datos-maestros/sincronizacion-aw/corridas";
        var clientesPolicy = PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesSincronizar;
        var productosPolicy = PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar;

        // Dos RequireAuthorization: se exigen AMBAS políticas (clientes y productos).
        app.MapPost(baseCorridas, async (
                AwClientesSincronizador clientes, AwProductosSincronizador productos, IntegracionesAwDbContext db,
                ICurrentUserContext user, ILoggerFactory loggerFactory, CancellationToken ct) =>
            {
                var actor = user.UserName ?? user.Email ?? user.UserId?.ToString() ?? "desconocido";
                var id = await clientes.IniciarBarridoAsync(actor, ct);
                string json;
                // ponytail: barrido de productos síncrono dentro del POST (~7k productos); moverlo al dispatcher si excede el timeout.
                try { json = JsonSerializer.Serialize(await productos.SincronizarBarridoAsync(ct, id), JsonResumen); }
                catch (AwProductosSyncException ex) { json = JsonSerializer.Serialize(new { error = ex.Code, mensaje = ex.Message }, JsonResumen); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // La ejecución de clientes ya está encolada: un fallo inesperado del barrido de productos no debe dar 500, queda en el resumen.
                    loggerFactory.CreateLogger("Millet.Api.SincronizacionAw").LogError(ex, "Falló el barrido de productos de la corrida {EjecucionId}.", id);
                    json = JsonSerializer.Serialize(new { error = "barrido_productos_fallido", mensaje = "El barrido de productos falló; revisa los registros del servidor." }, JsonResumen);
                }
                // Una sola columna: no choca con el token de concurrencia que usa el dispatcher al actualizar la ejecución.
                await db.ClientesEjecuciones.Where(e => e.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(e => e.ResumenProductos, json), ct);
                return Results.Accepted($"/api/v1/datos-maestros/clientes/sincronizacion/ejecuciones/{id}", new { id, estado = "Pendiente" });
            })
            .WithTags("DatosMaestros").RequireAuthorization(clientesPolicy).RequireAuthorization(productosPolicy)
            .WithMetadata(new RequireIdempotencyKeyAttribute());

        app.MapGet(baseCorridas + "/{id:guid}/diferencias", async (
                Guid id, IntegracionesAwDbContext awDb, CompartidoDbContext db, IAuthorizationService authorization,
                HttpContext httpContext, CancellationToken ct) =>
            {
                // La política exigida es la de clientes; lo de productos solo se incluye si el usuario también puede gestionarlos.
                var verProductos = (await authorization.AuthorizeAsync(httpContext.User, resource: null,
                    new PermissionRequirement(PermisosCanonicos.DatosMaestrosProductosAwGestionar))).Succeeded;
                var e = await awDb.ClientesEjecuciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new AwClientesSyncException("ejecucion_no_encontrada", "Ejecución de clientes inexistente.");
                var cambios = await db.CambiosMaestrosAw.AsNoTracking().Where(c => c.EjecucionId == id && (verProductos || c.TipoEntidad != "ProductoAw"))
                    .OrderBy(c => c.OcurridoEnUtc).Take(TopeFilasReporte + 1).ToListAsync(ct);
                var erroresClientes = await awDb.Set<Millet.Integraciones.Aw.Domain.AwClientesEjecucionError>().AsNoTracking()
                    .Where(x => x.EjecucionId == id).OrderBy(x => x.OcurridoEnUtc).Take(TopeFilasReporte + 1).ToListAsync(ct);
                var resumen = verProductos ? Leer(e.ResumenProductos) : new ResumenProductosJson();

                var filas = new List<IReadOnlyDictionary<string, string?>>();
                filas.AddRange(cambios.Select(c => Fila(c.TipoEntidad, c.ReferenciaExterna, c.Campo, c.ValorAntes, c.ValorDespues,
                    c.Dueno, c.Tipo.ToString(), c.Estado.ToString(), c.Causa, c.VersionEvento,
                    c.OcurridoEnUtc.ToString("o", CultureInfo.InvariantCulture))));
                filas.AddRange(erroresClientes.Select(x => Fila("Cliente", x.Referencia, null, null, null, null, "Error", null,
                    $"{x.Codigo}: {x.Mensaje}", null, x.OcurridoEnUtc.ToString("o", CultureInfo.InvariantCulture))));
                if (resumen.Error is null)
                    filas.AddRange((resumen.ErroresPorReferencia ?? []).Select(x => Fila("ProductoAw", x.Referencia, null, null, null, null,
                        "Error", null, $"{x.Codigo}: {x.Mensaje}", null, null)));
                var truncado = filas.Count > TopeFilasReporte;
                if (truncado) filas = filas.Take(TopeFilasReporte).ToList();

                var totales = new Dictionary<string, decimal>
                {
                    ["clientesLeidos"] = e.Leidos, ["clientesCreados"] = e.Creados, ["clientesActualizados"] = e.Actualizados,
                    ["clientesSinCambios"] = e.SinCambios, ["clientesConflictos"] = e.Conflictos, ["clientesErrores"] = e.Errores,
                    ["productosLeidos"] = resumen.Leidos, ["productosCreados"] = resumen.Creados,
                    ["productosActualizados"] = resumen.Actualizados, ["productosSinCambios"] = resumen.SinCambios,
                    ["productosErrores"] = resumen.Errores,
                    ["cambiosRegistrados"] = cambios.Count(c => c.Estado == EstadoCambioMaestroAw.Informativo),
                    ["conflictosPendientes"] = cambios.Count(c => c.Estado == EstadoCambioMaestroAw.Pendiente),
                    // RFC repetido entre clientes de A+W: va a revisión (Pendiente) y luego se resuelve; se cuenta en cualquier estado para el reporte de G1.10.
                    ["rfcRepetidosEntreAw"] = cambios.Count(c => c.Tipo == TipoCambioMaestroAw.RfcCompartido),
                };
                return Results.Ok(new ReporteDiferenciasAwDto(
                    $"Diferencias de la corrida del {e.IniciadaEnUtc.UtcDateTime.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} UTC",
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, string?> { ["Corrida"] = id.ToString(), ["Origen"] = e.Origen, ["Matriz"] = MatrizCampoDuenoAw.Version },
                    ColumnasReporte, filas, totales, truncado));
            })
            .WithTags("DatosMaestros").RequireAuthorization(clientesPolicy);
    }

    private static ResumenProductosJson Leer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<ResumenProductosJson>(json, JsonResumen) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static Dictionary<string, string?> Fila(string entidad, string referencia, string? campo, string? antes, string? despues,
        string? dueno, string tipo, string? estado, string? causa, string? version, string? ocurrido) => new()
    {
        ["entidad"] = entidad, ["referencia"] = referencia, ["campo"] = campo, ["antes"] = antes, ["despues"] = despues,
        ["dueno"] = dueno, ["tipo"] = tipo, ["estado"] = estado, ["causa"] = causa, ["version"] = version, ["ocurrido"] = ocurrido,
    };
}

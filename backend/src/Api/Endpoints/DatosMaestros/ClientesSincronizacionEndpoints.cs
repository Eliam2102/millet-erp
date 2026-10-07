using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.DatosMaestros;

public sealed record IniciarSincronizacionClientesRequest(string? Tipo);
public sealed record ReintentarClienteAwRequest(string? Referencia);

public sealed record EjecucionSincronizacionResumen(
    Guid Id, string Tipo, string Estado, int Leidos, int Creados, int Actualizados, int SinCambios,
    int Pendientes, int Conflictos, int Errores, DateTimeOffset IniciadaEnUtc, DateTimeOffset? TerminadaEnUtc,
    string Actor, string? ErrorGeneral);

public sealed record EjecucionSincronizacionError(
    string Referencia, string Codigo, string Mensaje, DateTimeOffset OcurridoEnUtc);

public sealed record EjecucionSincronizacionDetalle(
    EjecucionSincronizacionResumen Ejecucion, IReadOnlyList<EjecucionSincronizacionError> Errores, bool ErroresTruncados);

public sealed record ListarEjecucionesSincronizacionResponse(
    IReadOnlyList<EjecucionSincronizacionResumen> Items, int Total, int Offset, int Limit);

/// <summary>
/// ADM-06 Entrega D2: sincronización de clientes desde A+W. El POST de barrido solo ENCOLA
/// (202); lo ejecuta <c>AwClientesEjecucionDispatcher</c>. El reintento de una referencia es
/// inline y devuelve el estado real. Permiso: <c>datos_maestros.clientes.sincronizar</c>.
/// </summary>
public static class ClientesSincronizacionEndpoints
{
    private const int MaxErroresDetalle = 200;
    private const string Base = "/api/v1/datos-maestros/clientes/sincronizacion";

    public static IEndpointRouteBuilder MapClientesSincronizacionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Base).WithTags("DatosMaestros");
        var policy = PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosClientesSincronizar;

        group.MapPost("/ejecuciones", async (
            [FromBody] IniciarSincronizacionClientesRequest body,
            AwClientesSincronizador sync,
            ICurrentUserContext user,
            CancellationToken ct) =>
        {
            if (!string.Equals(body.Tipo, "Barrido", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("AW_CLIENTES_TIPO_INVALIDO", "Solo se admite tipo 'Barrido'.");
            var id = await sync.IniciarBarridoAsync(Actor(user), ct);
            return Results.Accepted($"{Base}/ejecuciones/{id}", new { id, estado = nameof(AwClientesEjecucionEstado.Pendiente) });
        })
        .RequireAuthorization(policy)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("IniciarSincronizacionClientesAw")
        .WithSummary("Encolar un barrido de clientes A+W (no ejecuta inline)")
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/ejecuciones", async (
            [FromQuery] AwClientesEjecucionEstado? estado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IntegracionesAwDbContext db,
            CancellationToken ct) =>
        {
            var off = Math.Max(0, offset ?? 0);
            var lim = Math.Clamp(limit ?? 20, 1, 100);
            var q = db.ClientesEjecuciones.AsNoTracking();
            if (estado is not null) q = q.Where(e => e.Estado == estado);
            var total = await q.CountAsync(ct);
            var items = await q.OrderByDescending(e => e.IniciadaEnUtc).Skip(off).Take(lim).ToListAsync(ct);
            return Results.Ok(new ListarEjecucionesSincronizacionResponse(items.Select(Resumen).ToList(), total, off, lim));
        })
        .RequireAuthorization(policy)
        .WithName("ListarEjecucionesSincronizacionClientesAw")
        .WithSummary("Listar ejecuciones de sincronización de clientes A+W")
        .Produces<ListarEjecucionesSincronizacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/ejecuciones/{id:guid}", async (Guid id, IntegracionesAwDbContext db, CancellationToken ct) =>
            Results.Ok(await DetalleAsync(db, id, ct)))
        .RequireAuthorization(policy)
        .WithName("ObtenerEjecucionSincronizacionClientesAw")
        .WithSummary("Detalle de una ejecución (contadores y hasta 200 errores por referencia)")
        .Produces<EjecucionSincronizacionDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/reintentos", async (
            [FromBody] ReintentarClienteAwRequest body,
            AwClientesSincronizador sync,
            IntegracionesAwDbContext db,
            ICurrentUserContext user,
            CancellationToken ct) =>
        {
            var id = await sync.ReintentarReferenciaAsync(body.Referencia ?? string.Empty, Actor(user), ct);
            return Results.Ok(await DetalleAsync(db, id, ct));
        })
        .RequireAuthorization(policy)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReintentarClienteAw")
        .WithSummary("Relee una referencia del origen y la aplica (inline, estado real)")
        .Produces<EjecucionSincronizacionDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static string Actor(ICurrentUserContext user) =>
        user.UserName ?? user.Email ?? user.UserId?.ToString() ?? "desconocido";

    private static async Task<EjecucionSincronizacionDetalle> DetalleAsync(
        IntegracionesAwDbContext db, Guid id, CancellationToken ct)
    {
        var e = await db.ClientesEjecuciones.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new AwClientesSyncException("ejecucion_no_encontrada", "Ejecución de clientes inexistente.");
        var errores = await db.Set<AwClientesEjecucionError>().AsNoTracking()
            .Where(x => x.EjecucionId == id)
            .OrderBy(x => x.OcurridoEnUtc).ThenBy(x => x.Referencia)
            .Take(MaxErroresDetalle + 1)
            .Select(x => new EjecucionSincronizacionError(x.Referencia, x.Codigo, x.Mensaje, x.OcurridoEnUtc))
            .ToListAsync(ct);
        return new(Resumen(e), errores.Take(MaxErroresDetalle).ToList(), errores.Count > MaxErroresDetalle);
    }

    private static EjecucionSincronizacionResumen Resumen(AwClientesEjecucion e) => new(
        e.Id, e.Tipo.ToString(), e.Estado.ToString(), e.Leidos, e.Creados, e.Actualizados, e.SinCambios,
        e.Pendientes, e.Conflictos, e.Errores, e.IniciadaEnUtc, e.TerminadaEnUtc, e.Actor, e.ErrorGeneral);
}

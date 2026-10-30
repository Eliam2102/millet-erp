using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Abstractions;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.PiezasPt;
using Millet.ProductoTerminado.Application;
using Millet.ProductoTerminado.Infrastructure;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.ProductoTerminado;
public static class ProductoTerminadoEndpoints
{
    public static void MapProductoTerminado(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/producto-terminado").WithTags("Producto terminado").RequireAuthorization();
        g.MapGet("/piezas", async (string? pedido, Guid? sucursalId, int? page, ProductoTerminadoDbContext db,
            DocumentoSucursalScope scope, ICurrentUserContext user, ICurrentUserPermissions permissions, IUsuarioSucursalReadPort branches, CancellationToken ct) =>
        {
            var allowed = await AllowedAsync(sucursalId, PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales, scope, user, permissions, branches, ct);
            var q = from p in db.Pieces.AsNoTracking() join o in db.Orders on p.PedidoPtId equals o.Id
                where (pedido == null || o.NumeroPedido == pedido) && (sucursalId == null || p.SucursalId == sucursalId)
                    && (allowed == null || allowed.Contains(p.SucursalId))
                select new { p.Id, Pedido = o.NumeroPedido, p.Posicion, p.NumeroPieza, p.SucursalId, p.EscaneadaAt, p.Estado, p.EstatusAw };
            return Results.Ok(new { Items = await q.OrderByDescending(x => x.EscaneadaAt).ThenBy(x => x.Id).Skip(PageOffset(page)).Take(10).ToListAsync(ct), Total = await q.CountAsync(ct) });
        }).RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoPiezasVer);
        g.MapGet("/escaneos", async (string? pedido, Guid? sucursalId, int? page, ProductoTerminadoDbContext db,
            DocumentoSucursalScope scope, ICurrentUserContext user, ICurrentUserPermissions permissions, IUsuarioSucursalReadPort branches, CancellationToken ct) =>
        {
            var allowed = await AllowedAsync(sucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, scope, user, permissions, branches, ct);
            var q = db.Scans.AsNoTracking().Where(s => (s.Resultado == "Pospuesto" || s.Resultado == "Excepcion")
                && (pedido == null || s.NumeroPedido == pedido) && (sucursalId == null || s.SucursalId == sucursalId)
                && (allowed == null || (s.SucursalId != null && allowed.Contains(s.SucursalId.Value))));
            var rows = await q.OrderBy(x => x.FirstAttemptAt).ThenBy(x => x.Id).Skip(PageOffset(page)).Take(10).ToListAsync(ct);
            return Results.Ok(new { Items = rows.Select(s => new { s.Id, s.NumeroPedido, s.Posicion, s.NumeroPieza, s.SucursalId, s.Resultado, s.Motivo, s.Intentos, s.Version }), Total = await q.CountAsync(ct) });
        }).RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar);
        var scan = g.MapGroup("/escaneos/{id:guid}").WithDocumentoSucursalScope("escaneo_pt", "producto_terminado.excepciones")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ProductoTerminadoExcepcionesGestionar);
        scan.MapGet("/", async (Guid id, ProductoTerminadoDbContext db, HttpResponse res, CancellationToken ct) =>
        {
            var value = await db.Scans.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new EntityNotFoundException("Escaneo PT", id.ToString());
            res.Headers.ETag = $"\"{value.Version}\""; return Results.Ok(value);
        });
        scan.MapPost("/reintentar", RetryAsync).WithMetadata(new RequireIdempotencyKeyAttribute());
        scan.MapPost("/descartar", DiscardAsync).WithMetadata(new RequireIdempotencyKeyAttribute());
    }
    private static int PageOffset(int? page)
    {
        if (page is < 1 or > 100000) throw new BusinessRuleException("PT_PAGINA_INVALIDA", "La página debe estar entre 1 y 100000.");
        return ((page ?? 1) - 1) * 10;
    }
    private static async Task<Guid[]?> AllowedAsync(Guid? branch, string bypass, DocumentoSucursalScope scope, ICurrentUserContext user,
        ICurrentUserPermissions permissions, IUsuarioSucursalReadPort branches, CancellationToken ct)
    {
        if (branch is Guid id) await scope.VerificarSucursalAsync(id, bypass, ct);
        return await permissions.TieneAsync(bypass, ct) ? null : user.UserId is Guid uid ? (await branches.ListarIdsAsync(uid, ct)).ToArray() : [];
    }
    private static Task<IResult> DiscardAsync(Guid id, ReasonBody body, HttpRequest req, HttpResponse res, ProductoTerminadoDbContext db,
        PtScanHandler handler, ICurrentUserContext user, IClock clock, CancellationToken ct) =>
        ResolveAsync(id, body, req, res, db, handler, user, clock, true, null, null, ct);
    private static Task<IResult> RetryAsync(Guid id, ReasonBody body, HttpRequest req, HttpResponse res, ProductoTerminadoDbContext db,
        PtScanHandler handler, ICurrentUserContext user, IClock clock, IOptions<PiezasPtOptions> options, ISucursalPorClaveAwResolver resolver, CancellationToken ct) =>
        ResolveAsync(id, body, req, res, db, handler, user, clock, false, options.Value, resolver, ct);
    private static async Task<IResult> ResolveAsync(Guid id, ReasonBody body, HttpRequest req, HttpResponse res, ProductoTerminadoDbContext db,
        PtScanHandler handler, ICurrentUserContext user, IClock clock, bool discard, PiezasPtOptions? options, ISucursalPorClaveAwResolver? resolver, CancellationToken ct)
    {
        var raw = req.Headers.IfMatch.ToString();
        if (raw.Length < 3 || raw[0] != '"' || raw[^1] != '"' || !int.TryParse(raw.AsSpan(1, raw.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1)
            return Results.Problem(statusCode: 428, title: "Envíe If-Match con la versión vigente del escaneo.", extensions: new Dictionary<string, object?> { ["code"] = "IF_MATCH_REQUIRED" });
        var scan = await db.Scans.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new EntityNotFoundException("Escaneo PT", id.ToString());
        await using var tx = await handler.BeginAsync(scan.EmpresaId, ct); await db.Entry(scan).ReloadAsync(ct);
        if (scan.Version != version) throw new ConcurrencyException("Escaneo PT", id);
        scan.Resolve(user.UserId ?? throw new ForbiddenException("PT_USUARIO_REQUERIDO", "Inicie sesión para resolver el escaneo."), body.Motivo, clock.UtcNow, discard);
        if (!discard)
        {
            var parts = scan.OrigenEventoId.Split('|');
            var station = parts.Length == 3 && int.TryParse(parts[1], out var number) ? options!.EstacionesPt.SingleOrDefault(s => s.Estacion == number) : null;
            if (station is not null) scan.SetBranch(station.SucursalClaveAw, await resolver!.ResolverAsync(station.SucursalClaveAw, ct));
            await handler.ApplyAsync(scan, ct);
        }
        await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        res.Headers.ETag = $"\"{scan.Version}\""; return Results.Ok(scan.ToResult());
    }
    public sealed record ReasonBody(string Motivo);
}

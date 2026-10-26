using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Api.Endpoints.Adjuntos;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.ActivosFijos.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using P = Millet.Identidad.Domain.PermisosCanonicos;
namespace Millet.Api.Endpoints.ActivosFijos;
public static class ConservacionEndpoints
{
    public static void MapConservacion(this RouteGroupBuilder g)
    {
        g.MapGroup("/activos/{id:guid}").MapAdjuntos("activo_fijo", P.ActivosFijosActivosVer, P.ActivosFijosDocumentosGestionar, P.ActivosFijosDocumentosGestionar);
        g.MapGet("/activos/{id:guid}/documentos", async (Guid id, ActivoServicio s, ActivosFijosDbContext db, CancellationToken ct) =>
        { await s.CargarAsync(id, false, ct); return Results.Ok(await db.Documentos.Where(x => x.ActivoId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)); }).RequireAuthorization(Policy(P.ActivosFijosActivosVer));
        g.MapPost("/activos/{id:guid}/documentos", async (Guid id, DocumentoActivoDatos d, HttpRequest req, IMediator m, CancellationToken ct) =>
            Version(req) is int v ? Results.Ok(await m.Send(new RegistrarDocumentoActivo(id, v, d), ct)) : FaltaVersion())
            .RequireAuthorization(Policy(P.ActivosFijosDocumentosGestionar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapGet("/activos/{id:guid}/mantenimientos", async (Guid id, ActivoServicio s, ActivosFijosDbContext db, CancellationToken ct) =>
        { await s.CargarAsync(id, false, ct); return Results.Ok(await db.Mantenimientos.Where(x => x.ActivoId == id).OrderByDescending(x => x.Fecha).ToListAsync(ct)); }).RequireAuthorization(Policy(P.ActivosFijosActivosVer));
        g.MapPost("/activos/{id:guid}/mantenimientos", async (Guid id, MantenimientoDatos d, HttpRequest req, IMediator m, CancellationToken ct) =>
            Version(req) is int v ? Results.Ok(await m.Send(new RegistrarMantenimientoActivo(id, v, d), ct)) : FaltaVersion())
            .RequireAuthorization(Policy(P.ActivosFijosMantenimientosGestionar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapGet("/activos/{id:guid}/facturas-mantenimiento", async (Guid id, string? q, ActivoServicio s, Millet.CuentasPorPagar.Application.PublicPorts.ICapitalizacionProveedorReadPort facturas, CancellationToken ct) =>
        { var a = await s.CargarAsync(id, true, ct); return Results.Ok(await facturas.BuscarAsync(false, null, a.SucursalId, q, ct)); }).RequireAuthorization(Policy(P.ActivosFijosMantenimientosGestionar));
        g.MapGet("/avisos", async (Guid? sucursalId, int? offset, ActivoServicio servicio, ActivosFijosDbContext db, ICatalogosActivoReadPort catalogos, IClock clock, CancellationToken ct) =>
        {
            var ids = await servicio.SucursalesAsync(sucursalId, ct);
            var responsables = await catalogos.ResponsablesAsync(servicio.Actor, ct);
            var q = from aviso in db.Avisos join activo in db.Activos on aviso.ActivoId equals activo.Id
                where responsables.Contains(aviso.ResponsableId) && (ids == null || ids.Contains(activo.SucursalId))
                orderby aviso.FechaAviso descending, activo.Numero
                select new VencimientoDato(aviso.Id, activo.Id, activo.Numero, activo.Descripcion, aviso.ResponsableId, activo.SucursalId,
                    aviso.EsMantenimiento ? "Próximo mantenimiento" : "Vencimiento de documento", aviso.Vencimiento, aviso.EsMantenimiento);
            return Results.Ok(new { items = await q.Skip(Math.Max(0, offset ?? 0)).Take(10).ToListAsync(ct), total = await q.CountAsync(ct), hoy = clock.HoyLocal() });
        }).RequireAuthorization(Policy(P.ActivosFijosActivosVer));
        g.MapGet("/por-vencer", PorVencer).RequireAuthorization(Policy(P.ActivosFijosActivosVer));
        g.MapGet("/conteos", async (Guid? sucursalId, int? offset, ActivoServicio s, ActivosFijosDbContext db, CancellationToken ct) =>
        {
            var ids = await s.SucursalesAsync(sucursalId, ct); var q = db.Conteos.AsNoTracking().Where(x => ids == null || ids.Contains(x.SucursalId));
            return Results.Ok(new { items = await q.OrderByDescending(x => x.CreatedAt).Skip(Math.Max(0, offset ?? 0)).Take(10).ToListAsync(ct), total = await q.CountAsync(ct) });
        }).RequireAuthorization(Policy(P.ActivosFijosActivosVer));
        g.MapGet("/conteos/{id:guid}", async (Guid id, HttpResponse response, ActivoServicio s, ActivosFijosDbContext db, CancellationToken ct) =>
        {
            var c = await db.Conteos.Include(x => x.Partidas).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new EntityNotFoundException("AF_CONTEO_NO_EXISTE", "No existe el conteo.");
            await s.SucursalAsync(c.SucursalId, false, ct); return Respuesta(c, response);
        }).RequireAuthorization(Policy(P.ActivosFijosActivosVer));
        g.MapPost("/conteos", async (AbrirConteoActivo d, IMediator m, HttpResponse response, CancellationToken ct) => Respuesta(await m.Send(d, ct), response))
            .RequireAuthorization(Policy(P.ActivosFijosConteosGestionar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        foreach (var accion in new[] { "capturar", "cerrar", "autorizar" })
            g.MapPost("/conteos/{id:guid}/" + accion, async (Guid id, MutacionConteo d, HttpRequest req, HttpResponse response, IMediator m, CancellationToken ct) =>
                Version(req) is int v ? Respuesta(await m.Send(new MutarConteoActivo(id, v, accion, d.Captura, d.PartidaId, d.Evidencia), ct), response) : FaltaVersion())
                .RequireAuthorization(Policy(accion == "autorizar" ? P.ActivosFijosConteosAutorizar : P.ActivosFijosConteosGestionar)).WithMetadata(new RequireIdempotencyKeyAttribute());
        g.MapGet("/etiquetas", Etiquetas).RequireAuthorization(Policy(P.ActivosFijosEtiquetasImprimir));
        g.MapPost("/etiquetas/registrar-impresion", async (ImpresionDatos datos, ActivoServicio s, ActivosFijosDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (datos.Activos == null || datos.Activos.Length is < 1 or > 1000 || datos.Activos.Any(x => x == null || x.Id == Guid.Empty || x.Version < 1) || datos.Activos.Select(x => x.Id).Distinct().Count() != datos.Activos.Length)
                throw new BusinessRuleException("AF_LOTE_INVALIDO", "Seleccione entre 1 y 1000 activos distintos.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            foreach (var item in datos.Activos.OrderBy(x => x.Id))
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({item.Id.ToString()}, 0))", ct);
                var a = await s.CargarAsync(item.Id, true, ct);
                if (a.Version != item.Version) throw new ConcurrencyException("Activo", a.Id);
                a.RegistrarImpresion(clock.UtcNow);
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.NoContent();
        }).RequireAuthorization(Policy(P.ActivosFijosEtiquetasImprimir)).WithMetadata(new RequireIdempotencyKeyAttribute());
    }
    private static async Task<IResult> Etiquetas(Guid? activoId, Guid? sucursalId, Guid? claseId, ActivoServicio s, ActivosFijosDbContext db, ICatalogosActivoReadPort catalogos, IConservacionReadPort lectura, CancellationToken ct)
    {
        var ids = await s.SucursalesAsync(sucursalId, ct);
        if (activoId is Guid id) await s.CargarAsync(id, false, ct);
        var q = db.Activos.AsNoTracking().Where(x => (ids == null || ids.Contains(x.SucursalId)) && (activoId == null || x.Id == activoId) && (claseId == null || x.ClaseId == claseId));
        if (await q.CountAsync(ct) > 1000) throw new BusinessRuleException("AF_LOTE_EXCEDIDO", "Filtre por sucursal o clase para imprimir hasta 1000 etiquetas por lote.");
        var resultado = new List<EtiquetaDato>();
        foreach (var a in await q.OrderBy(x => x.Numero).ToListAsync(ct))
        {
            var nombres = await catalogos.DescribirAsync(a.Custodia(), ct);
            resultado.Add(new(a.Id, a.Version, a.Numero, a.Descripcion, nombres.Sucursal ?? "Sin sucursal", await lectura.EmpresaAsync(a.EmpresaId, ct)));
        }
        return Results.Ok(resultado);
    }
    private static async Task<IResult> PorVencer(Guid? sucursalId, int? offset, ActivoServicio s, ActivosFijosDbContext db, IClock clock, ICurrentEmpresaContext empresa, IOptions<AvisosActivosOptions> opciones, CancellationToken ct)
    {
        var ids = await s.SucursalesAsync(sucursalId, ct); var hoy = clock.HoyLocal(); var hasta = hoy.AddDays(opciones.Value.Dias(empresa.Current!.Value));
        var lista = ConservacionConsultas.PorVencer(db, ids, hasta);
        return Results.Ok(new { items = await lista.Skip(Math.Max(0, offset ?? 0)).Take(10).ToListAsync(ct), total = await lista.CountAsync(ct), hoy });
    }
    private static string Policy(string p) => PermissionPolicyProvider.Prefix + p;
    private static int? Version(HttpRequest req) => int.TryParse(req.Headers.IfMatch.ToString().Trim('"'), out var v) && v > 0 ? v : null;
    private static IResult FaltaVersion() => Results.Problem(statusCode: 428, title: "Envíe If-Match con el ETag vigente.");
    private static IResult Respuesta(ConteoActivo c, HttpResponse response) { response.Headers.ETag = $"\"{c.Version}\""; return Results.Ok(c); }
    public sealed record MutacionConteo(CapturaConteoDatos? Captura = null, Guid? PartidaId = null, string? Evidencia = null);
    public sealed record ImpresionItem(Guid Id, int Version);
    public sealed record ImpresionDatos(ImpresionItem[] Activos);
    public sealed record EtiquetaDato(Guid Id, int Version, string Numero, string Descripcion, string Sucursal, string Empresa);
}

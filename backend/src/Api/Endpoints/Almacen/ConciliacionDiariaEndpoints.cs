using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Almacen.Application.Conciliacion;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Conciliacion;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Conciliación diaria del ERP contra A+W (A4.6). Correr la conciliación y aclarar una diferencia con la mesa de corte piden
/// <c>almacen.inventarios.crear</c>; consultar la bandeja y el histórico, <c>almacen.inventarios.leer</c>. Alcance por sucursal
/// del almacén (ADR-0051).
/// </summary>
public static class ConciliacionDiariaEndpoints
{
    public sealed record FisicoLineaRequest(Guid ArticuloId, Guid? ProductoAwVarianteId, string? Lote, decimal Fisico);
    public sealed record EjecutarConciliacionRequest(Guid AlmacenId, DateOnly? Fecha, string Fuente, List<FisicoLineaRequest> Lineas);
    public sealed record AclararDiferenciaRequest(string Motivo);

    public sealed record DiferenciaResponse(Guid Id, Guid ConciliacionId, Guid AlmacenId, string? AlmacenClave, DateOnly Fecha,
        Guid ArticuloId, string? ArticuloClave, string? ArticuloDescripcion, Guid? ProductoAwVarianteId, string? Medida, string? Lote,
        string Unidad, decimal Teorico, decimal Fisico, decimal Diferencia, string Sentido, decimal CostoUnitarioMxn, decimal ValorMxn,
        string? FolioMovimiento, string Estado, string? MotivoAclaracion, DateTimeOffset? AclaradaAt);

    public sealed record ExistenciaLineaResponse(Guid ArticuloId, string? ArticuloClave, string? ArticuloDescripcion,
        Guid? ProductoAwVarianteId, string? Medida, string? Lote, string Unidad, decimal Teorico);

    public sealed record CorridaResponse(Guid Id, Guid AlmacenId, string? AlmacenClave, DateOnly Fecha, string Fuente,
        int LineasComparadas, int Diferencias, DateTimeOffset EjecutadaAt, List<DiferenciaResponse> Detalle);

    public static IEndpointRouteBuilder MapConciliacionDiariaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/conciliacion-diaria").WithTags("Almacen");

        group.MapPost("/", async (EjecutarConciliacionRequest req, AlmacenDbContext db, IIntegrationEventPublisher events,
            IVarianteVidrioReadPort variantes, IArticuloReadPort articulos, IPeriodoContableReadPort periodo,
            DocumentoSucursalScope scope, ICurrentUserContext user, ICurrentEmpresaContext empresa, CancellationToken ct) =>
        {
            var almacen = await db.Almacenes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == req.AlmacenId, ct)
                ?? throw new EntityNotFoundException("ALMACEN_NO_ENCONTRADO", $"No existe el almacén '{req.AlmacenId}'.");
            await scope.VerificarSucursalAsync(almacen.SucursalId, PermisosCanonicos.AlmacenInventariosGestionarTodasSucursales, ct);
            var empresaId = empresa.Current ?? throw new BusinessRuleException("CONCILIACION_SIN_EMPRESA", "Contexto de empresa requerido.");

            await using var tx = db.Database.IsNpgsql() ? await db.Database.BeginTransactionAsync(ct) : null;
            var (corrida, difs) = await ConciliacionDiariaApp.EjecutarAsync(db, events, variantes, articulos, periodo, empresaId,
                user.UserId ?? Guid.Empty, req.AlmacenId, req.Fecha, req.Fuente,
                (req.Lineas ?? []).Select(l => new FisicoLinea(l.ArticuloId, l.ProductoAwVarianteId, l.Lote, l.Fisico)).ToList(), ct);
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return Results.Ok(new CorridaResponse(corrida.Id, corrida.AlmacenId, almacen.Clave, corrida.Fecha, corrida.Fuente,
                corrida.LineasComparadas, corrida.Diferencias, corrida.CreatedAt, await Respuestas(db, articulos, variantes, difs, ct)));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCrear)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("EjecutarConciliacionDiaria")
        .Produces<CorridaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Existencia teórica del almacén por hoja/medida/lote: la lista contra la que se captura el físico.
        group.MapGet("/existencia", async ([FromQuery] Guid almacenId, AlmacenDbContext db, IArticuloReadPort articulos,
            IVarianteVidrioReadPort variantes, DocumentoSucursalScope scope, CancellationToken ct) =>
        {
            var almacen = await db.Almacenes.AsNoTracking().FirstOrDefaultAsync(a => a.Id == almacenId, ct)
                ?? throw new EntityNotFoundException("ALMACEN_NO_ENCONTRADO", $"No existe el almacén '{almacenId}'.");
            await scope.VerificarSucursalAsync(almacen.SucursalId, PermisosCanonicos.AlmacenInventariosLeerTodasSucursales, ct);
            var filas = await (from s in db.SaldosInventario.AsNoTracking()
                               join sub in db.SubAlmacenes on s.SubAlmacenId equals sub.Id
                               where sub.AlmacenId == almacenId && s.CantidadDisponible > 0
                                   && sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                               group s by new { s.ArticuloId, s.ProductoAwVarianteId, s.Lote } into g
                               select new { g.Key.ArticuloId, g.Key.ProductoAwVarianteId, g.Key.Lote, Cantidad = g.Sum(x => x.CantidadDisponible) })
                .ToListAsync(ct);
            var maestro = await articulos.ObtenerPorIdsAsync(filas.Select(f => f.ArticuloId).Distinct().ToList(), ct);
            var medidas = await variantes.ObtenerPorIdsAsync(
                filas.Where(f => f.ProductoAwVarianteId != null).Select(f => f.ProductoAwVarianteId!.Value).Distinct().ToList(), ct);
            return Results.Ok(filas.Select(f =>
            {
                var a = maestro.GetValueOrDefault(f.ArticuloId);
                var m = f.ProductoAwVarianteId is Guid v ? medidas.GetValueOrDefault(v) : null;
                var area = m?.AreaHojaM2;
                return new ExistenciaLineaResponse(f.ArticuloId, a?.Clave, a?.Descripcion, f.ProductoAwVarianteId,
                    m?.AnchoMm is { } w && m.AltoMm is { } h ? $"{w:0.##} x {h:0.##} mm" : null, f.Lote,
                    f.ProductoAwVarianteId is null ? a?.UnidadMedida ?? "PZA" : "hojas",
                    area is { } ar ? Math.Round(f.Cantidad / ar, 4) : f.Cantidad);
            }).OrderBy(x => x.ArticuloClave).ThenBy(x => x.Medida).ThenBy(x => x.Lote).ToList());
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosLeer)
        .WithName("ExistenciaTeoricaConciliacionDiaria")
        .Produces<List<ExistenciaLineaResponse>>(StatusCodes.Status200OK);

        // Histórico de corridas (también las que no dejaron diferencias, A4.6-a).
        group.MapGet("/corridas", async ([FromQuery] Guid? almacenId, AlmacenDbContext db, IArticuloReadPort articulos,
            IVarianteVidrioReadPort variantes, DocumentoSucursalScope scope, ICurrentUserContext user,
            ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, CancellationToken ct) =>
        {
            var propias = await SucursalesVisibles(permisos, sucursales, user, ct);
            var q = from c in db.ConciliacionesDiarias.AsNoTracking()
                    join a in db.Almacenes.AsNoTracking() on c.AlmacenId equals a.Id
                    where (almacenId == null || c.AlmacenId == almacenId) && (propias == null || propias.Contains(a.SucursalId))
                    orderby c.Fecha descending, c.CreatedAt descending
                    select new { Corrida = c, a.Clave };
            var filas = await q.Take(200).ToListAsync(ct);
            var ids = filas.Select(f => f.Corrida.Id).ToList();
            var difs = await db.DiferenciasConciliacion.AsNoTracking().Where(d => ids.Contains(d.ConciliacionId)).ToListAsync(ct);
            var detalle = (await Respuestas(db, articulos, variantes, difs, ct)).ToLookup(d => d.ConciliacionId);
            return Results.Ok(filas.Select(f => new CorridaResponse(f.Corrida.Id, f.Corrida.AlmacenId, f.Clave, f.Corrida.Fecha,
                f.Corrida.Fuente, f.Corrida.LineasComparadas, f.Corrida.Diferencias, f.Corrida.CreatedAt, detalle[f.Corrida.Id].ToList())).ToList());
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosLeer)
        .WithName("ListarCorridasConciliacionDiaria")
        .Produces<List<CorridaResponse>>(StatusCodes.Status200OK);

        // Bandeja de la mesa de corte (estado=PENDIENTE) e histórico de diferencias.
        group.MapGet("/diferencias", async ([FromQuery] string? estado, [FromQuery] string? sentido, AlmacenDbContext db,
            IArticuloReadPort articulos, IVarianteVidrioReadPort variantes, ICurrentUserContext user,
            ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, CancellationToken ct) =>
        {
            var propias = await SucursalesVisibles(permisos, sucursales, user, ct);
            var q = from d in db.DiferenciasConciliacion.AsNoTracking()
                    join a in db.Almacenes.AsNoTracking() on d.AlmacenId equals a.Id
                    where propias == null || propias.Contains(a.SucursalId)
                    select d;
            if (!string.IsNullOrWhiteSpace(estado)) q = q.Where(d => d.Estado == estado);
            if (!string.IsNullOrWhiteSpace(sentido)) q = q.Where(d => d.Sentido == sentido);
            var filas = await q.OrderByDescending(d => d.Fecha).ThenByDescending(d => d.CreatedAt).Take(500).ToListAsync(ct);
            return Results.Ok(await Respuestas(db, articulos, variantes, filas, ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosLeer)
        .WithName("ListarDiferenciasConciliacion")
        .Produces<List<DiferenciaResponse>>(StatusCodes.Status200OK);

        // Aclarar con la mesa de corte: solo anota el motivo; la salida ya descontó, no se descuenta otra vez.
        group.MapPost("/diferencias/{id:guid}/aclarar", async (Guid id, AclararDiferenciaRequest req, AlmacenDbContext db,
            IArticuloReadPort articulos, IVarianteVidrioReadPort variantes, DocumentoSucursalScope scope, ICurrentUserContext user,
            CancellationToken ct) =>
        {
            var d = await db.DiferenciasConciliacion.FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new EntityNotFoundException("DIFERENCIA_NO_ENCONTRADA", $"No existe la diferencia '{id}'.");
            var sucursalId = await db.Almacenes.Where(a => a.Id == d.AlmacenId).Select(a => a.SucursalId).SingleAsync(ct);
            await scope.VerificarSucursalAsync(sucursalId, PermisosCanonicos.AlmacenInventariosGestionarTodasSucursales, ct);
            d.Aclarar(user.UserId ?? Guid.Empty, req.Motivo);
            await db.SaveChangesAsync(ct);
            return Results.Ok((await Respuestas(db, articulos, variantes, [d], ct)).Single());
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCrear)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AclararDiferenciaConciliacion")
        .Produces<DiferenciaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>null = ve todas las sucursales.</summary>
    private static async Task<List<Guid>?> SucursalesVisibles(ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales,
        ICurrentUserContext user, CancellationToken ct) =>
        await permisos.TieneAsync(PermisosCanonicos.AlmacenInventariosLeerTodasSucursales, ct) ? null
            : user.UserId is { } uid ? (await sucursales.ListarIdsAsync(uid, ct)).ToList() : [];

    private static async Task<List<DiferenciaResponse>> Respuestas(AlmacenDbContext db, IArticuloReadPort articulos,
        IVarianteVidrioReadPort variantes, IReadOnlyCollection<DiferenciaConciliacion> filas, CancellationToken ct)
    {
        var maestro = await articulos.ObtenerPorIdsAsync(filas.Select(d => d.ArticuloId).Distinct().ToList(), ct);
        var medidas = await variantes.ObtenerPorIdsAsync(
            filas.Where(d => d.ProductoAwVarianteId != null).Select(d => d.ProductoAwVarianteId!.Value).Distinct().ToList(), ct);
        var almIds = filas.Select(d => d.AlmacenId).Distinct().ToList();
        var claves = await db.Almacenes.AsNoTracking().Where(a => almIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Clave, ct);
        return filas.Select(d =>
        {
            var a = maestro.GetValueOrDefault(d.ArticuloId);
            var m = d.ProductoAwVarianteId is Guid v ? medidas.GetValueOrDefault(v) : null;
            return new DiferenciaResponse(d.Id, d.ConciliacionId, d.AlmacenId, claves.GetValueOrDefault(d.AlmacenId), d.Fecha,
                d.ArticuloId, a?.Clave, a?.Descripcion, d.ProductoAwVarianteId,
                m?.AnchoMm is { } w && m.AltoMm is { } h ? $"{w:0.##} x {h:0.##} mm" : null, d.Lote,
                d.AreaHojaM2 is null ? a?.UnidadMedida ?? "PZA" : "hojas", d.Teorico, d.Fisico, d.Diferencia, d.Sentido,
                d.CostoUnitarioMxn, d.ValorMxn, d.FolioMovimiento, d.Estado, d.MotivoAclaracion, d.AclaradaAt);
        }).ToList();
    }
}

using MediatR;
using Millet.Administracion.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Correcciones;
using Millet.Almacen.Domain.Correcciones;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Corrección de consumo (M-334, M-331). Almacén la solicita con <c>almacen.devoluciones-internas.capturar</c> (sin efecto en el
/// saldo) y Costos la autoriza o la rechaza con <c>almacen.correcciones-consumo.autorizar</c> (otro usuario; Fernando Alejos, 11-nov).
/// Al autorizar se aplica la devolución interna ligada a la salida con fecha de hoy. Alcance por la sucursal del sub-almacén destino.
/// </summary>
public static class CorreccionesConsumoEndpoints
{
    public sealed record LineaCorreccionDto(Guid LineaSalidaOrigenId, decimal Cantidad, Guid UbicacionId);
    public sealed record SolicitarCorreccionRequest(Guid SalidaOrigenId, Guid SubAlmacenDestinoId, string Tipo, string Motivo,
        string? Observaciones, IReadOnlyList<LineaCorreccionDto> Lineas);
    public sealed record RechazarCorreccionRequest(string Motivo);
    public sealed record TipoCorreccionResponse(string Codigo, string Texto);

    public sealed record CorreccionResponse(Guid Id, Guid SalidaOrigenId, string FolioSalida, string Tipo, string TipoTexto, string Motivo,
        string? Observaciones, IReadOnlyList<LineaCorreccionDto> Lineas, decimal Cantidad, string Estado, string SolicitadoPor,
        DateTimeOffset SolicitadoAt, string? DecididoPor, DateTimeOffset? DecididoAt, string? MotivoRechazo, Guid? DevolucionId,
        string? FolioDevolucion);

    public sealed record SaldoLineaResponse(Guid LineaSalidaId, string? ArticuloClave, string? ArticuloDescripcion, string UnidadMedida,
        decimal Consumido, decimal Corregido, decimal Neto, decimal PendienteDeAutorizar);
    public sealed record CorreccionesSalidaResponse(Guid SalidaId, string? FolioSalida, decimal Consumido, decimal Corregido, decimal Neto,
        IReadOnlyList<SaldoLineaResponse> Lineas, IReadOnlyList<CorreccionResponse> Correcciones);

    public static IEndpointRouteBuilder MapCorreccionesConsumoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/correcciones-consumo").WithTags("Almacen");
        const string leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesInternasLeer;

        group.MapGet("/tipos", () => Results.Ok(CorreccionConsumo.Tipos.Select(t => new TipoCorreccionResponse(t.Key, t.Value)).ToList()))
            .RequireAuthorization(leer).WithName("ListarTiposCorreccionConsumo")
            .Produces<List<TipoCorreccionResponse>>(StatusCodes.Status200OK);

        group.MapGet("/", async ([FromQuery] string? estado, AlmacenDbContext db, ICurrentUserContext user, ICurrentUserPermissions permisos,
            IUsuarioSucursalReadPort sucursales, IUsuarioReadPort usuarios, CancellationToken ct) =>
        {
            var q = db.CorreccionesConsumo.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(estado)) q = q.Where(c => c.Estado == estado);
            if (!await permisos.TieneAsync(PermisosCanonicos.AlmacenDevolucionesInternasGestionarTodasSucursales, ct))
            {
                var propias = user.UserId is { } uid ? await sucursales.ListarIdsAsync(uid, ct) : [];
                var subs = from sa in db.SubAlmacenes join a in db.Almacenes on sa.AlmacenId equals a.Id
                           where propias.Contains(a.SucursalId) select sa.Id;
                q = q.Where(c => subs.Contains(c.SubAlmacenDestinoId));
            }
            var filas = await q.OrderByDescending(c => c.CreatedAt).Take(500).ToListAsync(ct);
            return Results.Ok(await RespuestasAsync(filas, usuarios, ct));
        }).RequireAuthorization(leer).WithName("ListarCorreccionesConsumo").Produces<List<CorreccionResponse>>(StatusCodes.Status200OK);

        // Vista de la salida: consumido, corregido y neto (M-334: «10 consumidas, 2 corregidas y 8 netas»).
        app.MapGet("/api/v1/almacen/salidas/{id:guid}/correcciones", async (Guid id, AlmacenDbContext db, IArticuloReadPort articulos,
            IUsuarioReadPort usuarios, CancellationToken ct) =>
        {
            var salida = await db.Movimientos.AsNoTracking().Include(m => m.Lineas).FirstOrDefaultAsync(m => m.Id == id, ct)
                ?? throw new EntityNotFoundException("SALIDA_NO_ENCONTRADA", $"No existe la salida '{id}'.");
            var saldos = await CorreccionConsumoApp.SaldosAsync(db, salida, ct);
            var maestro = await articulos.ObtenerPorIdsAsync(saldos.Select(s => s.ArticuloId).Distinct().ToList(), ct);
            var correcciones = await db.CorreccionesConsumo.AsNoTracking().Where(c => c.SalidaOrigenId == id)
                .OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
            return Results.Ok(new CorreccionesSalidaResponse(id, salida.Folio, saldos.Sum(s => s.Consumido), saldos.Sum(s => s.Corregido),
                saldos.Sum(s => s.Neto),
                saldos.Select(s => new SaldoLineaResponse(s.LineaSalidaId, maestro.GetValueOrDefault(s.ArticuloId)?.Clave,
                    maestro.GetValueOrDefault(s.ArticuloId)?.Descripcion, s.UnidadMedida, s.Consumido, s.Corregido, s.Neto, s.Pendiente)).ToList(),
                await RespuestasAsync(correcciones, usuarios, ct)));
        }).RequireAuthorization(leer).WithTags("Almacen").WithName("CorreccionesDeSalida")
            .Produces<CorreccionesSalidaResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (SolicitarCorreccionRequest req, AlmacenDbContext db, DocumentoSucursalScope scope, ICurrentUserContext user,
            ICurrentEmpresaContext empresa, IUsuarioReadPort usuarios, CancellationToken ct) =>
        {
            await scope.VerificarSucursalAsync(await SucursalAsync(db, req.SubAlmacenDestinoId, ct),
                PermisosCanonicos.AlmacenDevolucionesInternasGestionarTodasSucursales, ct);
            var c = await CorreccionConsumoApp.SolicitarAsync(db,
                empresa.Current ?? throw new BusinessRuleException("CORRECCION_SIN_EMPRESA", "El contexto de empresa es requerido."),
                user.UserId ?? Guid.Empty,
                new SolicitarCorreccionConsumo(req.SalidaOrigenId, req.SubAlmacenDestinoId, req.Tipo, req.Motivo, req.Observaciones,
                    req.Lineas.Select(l => new CorreccionConsumoLinea(l.LineaSalidaOrigenId, l.Cantidad, l.UbicacionId)).ToList()), ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/almacen/correcciones-consumo/{c.Id}", (await RespuestasAsync([c], usuarios, ct))[0]);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesInternasCapturar)
        .WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("SolicitarCorreccionConsumo")
        .Produces<CorreccionResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/autorizar", async (Guid id, AlmacenDbContext db, ISender mediator, DocumentoSucursalScope scope,
            ICurrentUserContext user, IUsuarioReadPort usuarios, CancellationToken ct) =>
        {
            var c = await CargarAsync(db, id, ct);
            await scope.VerificarSucursalAsync(await SucursalAsync(db, c.SubAlmacenDestinoId, ct),
                PermisosCanonicos.AlmacenDevolucionesInternasGestionarTodasSucursales, ct);
            await CorreccionConsumoApp.AutorizarAsync(db, mediator, c, user.UserId ?? Guid.Empty, DateOnly.FromDateTime(DateTime.Now), ct);
            return Results.Ok((await RespuestasAsync([c], usuarios, ct))[0]);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenCorreccionesConsumoAutorizar)
        .WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("AutorizarCorreccionConsumo")
        .Produces<CorreccionResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/rechazar", async (Guid id, RechazarCorreccionRequest req, AlmacenDbContext db, DocumentoSucursalScope scope,
            ICurrentUserContext user, IUsuarioReadPort usuarios, CancellationToken ct) =>
        {
            var c = await CargarAsync(db, id, ct);
            await scope.VerificarSucursalAsync(await SucursalAsync(db, c.SubAlmacenDestinoId, ct),
                PermisosCanonicos.AlmacenDevolucionesInternasGestionarTodasSucursales, ct);
            c.Rechazar(user.UserId ?? Guid.Empty, req.Motivo);
            await db.SaveChangesAsync(ct);
            return Results.Ok((await RespuestasAsync([c], usuarios, ct))[0]);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenCorreccionesConsumoAutorizar)
        .WithMetadata(new RequireIdempotencyKeyAttribute()).WithName("RechazarCorreccionConsumo")
        .Produces<CorreccionResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<Guid?> SucursalAsync(AlmacenDbContext db, Guid subAlmacenId, CancellationToken ct) =>
        (await AlmacenSucursalResolver.ResolverAsync(db, subAlmacenId, ct)).Item2;

    private static async Task<CorreccionConsumo> CargarAsync(AlmacenDbContext db, Guid id, CancellationToken ct) =>
        await db.CorreccionesConsumo.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new EntityNotFoundException("CORRECCION_NO_ENCONTRADA", $"No existe la corrección '{id}'.");

    private static async Task<List<CorreccionResponse>> RespuestasAsync(IReadOnlyList<CorreccionConsumo> filas, IUsuarioReadPort usuarios,
        CancellationToken ct)
    {
        var nombres = await usuarios.ObtenerNombresAsync(filas.SelectMany(c => new[] { c.SolicitadoPor, c.DecididoPor ?? Guid.Empty })
            .Where(g => g != Guid.Empty).Distinct().ToList(), ct);
        string Nombre(Guid g) => nombres.GetValueOrDefault(g) ?? g.ToString();
        return filas.Select(c => new CorreccionResponse(c.Id, c.SalidaOrigenId, c.FolioSalida, c.Tipo, CorreccionConsumo.Tipos[c.Tipo],
            c.Motivo, c.Observaciones, c.Lineas.Select(l => new LineaCorreccionDto(l.LineaSalidaOrigenId, l.Cantidad, l.UbicacionId)).ToList(),
            c.Lineas.Sum(l => l.Cantidad), c.Estado, Nombre(c.SolicitadoPor), c.CreatedAt, c.DecididoPor is { } d ? Nombre(d) : null,
            c.DecididoAt, c.MotivoRechazo, c.DevolucionId, c.FolioDevolucion)).ToList();
    }
}

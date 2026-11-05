using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Mermas;
using Millet.Almacen.Domain.Mermas;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Desperdicio de mesa de corte y merma por etapa y motivo (A4.4). Lo registra A+W por la app de Almacenes (mensajes
/// DESPERDICIO_MESA y MERMA_PROCESO); aquí se consulta y la merma pendiente se autoriza o se rechaza. Lectura con el permiso
/// de la bandeja de salidas; autorizar y rechazar con <c>almacen.mermas.autorizar</c>. Alcance por sucursal (ADR-0051).
/// </summary>
public static class MermasProcesoEndpoints
{
    public sealed record MermaProcesoResponse(Guid Id, Guid SucursalId, string Tipo, DateOnly Fecha, string? Mesa, string Etapa,
        string Motivo, Guid ArticuloId, string? ArticuloClave, string? ArticuloDescripcion, decimal? Kg, decimal M2,
        decimal CostoUnitarioM2Mxn, decimal ValorMxn, string Estado, string? AutorizadoPor, DateTimeOffset? AutorizadoAt, string? MotivoRechazo);

    public sealed record RechazarMermaRequest(string Motivo);

    public static IEndpointRouteBuilder MapMermasProcesoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/mermas-proceso").WithTags("Almacen");

        group.MapGet("/", async ([FromQuery] string? estado, [FromQuery] string? tipo, [FromQuery] Guid? sucursalId,
            AlmacenDbContext db, IArticuloReadPort articulos, DocumentoSucursalScope scope, ICurrentUserContext user,
            ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales, CancellationToken ct) =>
        {
            var q = db.MermasProceso.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(estado)) q = q.Where(m => m.Estado == estado);
            if (!string.IsNullOrWhiteSpace(tipo)) q = q.Where(m => m.Tipo == tipo);
            if (sucursalId is { } s)
            {
                await scope.VerificarSucursalAsync(s, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales, ct);
                q = q.Where(m => m.SucursalId == s);
            }
            else if (!await permisos.TieneAsync(PermisosCanonicos.AlmacenSalidasLeerTodasSucursales, ct))
            {
                var propias = user.UserId is { } uid ? await sucursales.ListarIdsAsync(uid, ct) : [];
                q = q.Where(m => propias.Contains(m.SucursalId));
            }
            var filas = await q.OrderByDescending(m => m.Fecha).ThenByDescending(m => m.CreatedAt).Take(500).ToListAsync(ct);
            var maestro = await articulos.ObtenerPorIdsAsync(filas.Select(m => m.ArticuloId).Distinct().ToList(), ct);
            return Results.Ok(filas.Select(m => Respuesta(m, maestro.GetValueOrDefault(m.ArticuloId))).ToList());
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ListarMermasProceso")
        .Produces<List<MermaProcesoResponse>>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/autorizar", async (Guid id, AlmacenDbContext db, IIntegrationEventPublisher events,
            IArticuloReadPort articulos, DocumentoSucursalScope scope, ICurrentUserContext user, CancellationToken ct) =>
        {
            var m = await Cargar(db, id, ct);
            await scope.VerificarSucursalAsync(m.SucursalId, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, ct);
            await MermaProcesoApp.AutorizarAsync(events, m, user.UserId ?? Guid.Empty, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(Respuesta(m, await articulos.ObtenerAsync(m.ArticuloId, ct)));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenMermasAutorizar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AutorizarMermaProceso")
        .Produces<MermaProcesoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/rechazar", async (Guid id, RechazarMermaRequest req, AlmacenDbContext db,
            IArticuloReadPort articulos, DocumentoSucursalScope scope, ICurrentUserContext user, CancellationToken ct) =>
        {
            var m = await Cargar(db, id, ct);
            await scope.VerificarSucursalAsync(m.SucursalId, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, ct);
            m.Rechazar(user.UserId ?? Guid.Empty, req.Motivo);
            await db.SaveChangesAsync(ct);
            return Results.Ok(Respuesta(m, await articulos.ObtenerAsync(m.ArticuloId, ct)));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenMermasAutorizar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RechazarMermaProceso")
        .Produces<MermaProcesoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task<MermaProceso> Cargar(AlmacenDbContext db, Guid id, CancellationToken ct) =>
        await db.MermasProceso.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("MERMA_NO_ENCONTRADA", $"No existe una merma con id '{id}'.");

    private static MermaProcesoResponse Respuesta(MermaProceso m, ArticuloLectura? a) =>
        new(m.Id, m.SucursalId, m.Tipo, m.Fecha, m.Mesa, m.Etapa, m.Motivo, m.ArticuloId, a?.Clave, a?.Descripcion, m.Kg, m.M2,
            m.CostoUnitarioM2Mxn, m.ValorMxn, m.Estado, m.AutorizadoPor is null ? null : m.UpdatedBy, m.AutorizadoAt, m.MotivoRechazo);
}

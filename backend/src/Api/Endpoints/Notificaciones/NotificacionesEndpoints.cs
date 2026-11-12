using System.Data.Common;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Infrastructure;
using Millet.Identidad.Infrastructure.Notificaciones;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Notificaciones;

namespace Millet.Api.Endpoints.Notificaciones;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LeerAvisoSolicitud(Guid? SucursalId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PreferenciaSonidoSolicitud(bool? Habilitado);

public static class NotificacionesEndpoints
{
    public static IEndpointRouteBuilder MapNotificacionesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/notificaciones").RequireAuthorization().WithTags("Notificaciones");
        g.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(ctx);
        });
        g.MapPost("/sincronizar", async (SincronizarAvisosSolicitud body, CentroAvisos centro,
            ICurrentUserContext user, ICurrentEmpresaContext empresa, IdentidadDbContext identidad,
            IUsuarioSucursalReadPort sucursales, ICurrentUserPermissions permisos, CancellationToken ct) =>
        {
            var contexto = await ContextoAsync(body.SucursalId, user, empresa, identidad, sucursales, permisos, ct);
            try { return Results.Ok(await centro.SincronizarAsync(contexto, body, ct)); }
            catch (DbException) { return Results.Problem(statusCode: 503, title: "No se pudieron consultar los avisos. Reintenta."); }
        }).WithName("SincronizarAvisosUsuario");
        g.MapPost("/{clave}/leer", async (string clave, LeerAvisoSolicitud body, CentroAvisos centro,
            ICurrentUserContext user, ICurrentEmpresaContext empresa, IdentidadDbContext identidad,
            IUsuarioSucursalReadPort sucursales, ICurrentUserPermissions permisos, CancellationToken ct) =>
        {
            var contexto = await ContextoAsync(body.SucursalId, user, empresa, identidad, sucursales, permisos, ct);
            return await centro.MarcarLeidoAsync(contexto, clave, ct) ? Results.NoContent() : Results.NotFound();
        }).WithName("MarcarAvisoUsuarioLeido");
        g.MapGet("/preferencia-sonido", async (PreferenciaSonidoAdapter preferencia, ICurrentUserContext user,
            ICurrentEmpresaContext empresa, IdentidadDbContext identidad, IUsuarioSucursalReadPort sucursales,
            ICurrentUserPermissions permisos, CancellationToken ct) =>
        {
            var contexto = await ContextoAsync(null, user, empresa, identidad, sucursales, permisos, ct);
            return Results.Ok(new { Habilitado = await preferencia.ObtenerAsync(contexto.UsuarioId, ct) });
        });
        g.MapPut("/preferencia-sonido", async (PreferenciaSonidoSolicitud body, PreferenciaSonidoAdapter preferencia,
            ICurrentUserContext user, ICurrentEmpresaContext empresa, IdentidadDbContext identidad,
            IUsuarioSucursalReadPort sucursales, ICurrentUserPermissions permisos, CancellationToken ct) =>
        {
            if (body.Habilitado is null) throw new BusinessRuleException("AVISOS_PREFERENCIA_INVALIDA", "Indica si deseas habilitar el sonido.");
            var contexto = await ContextoAsync(null, user, empresa, identidad, sucursales, permisos, ct);
            await preferencia.GuardarAsync(contexto.UsuarioId, body.Habilitado.Value, ct);
            return Results.NoContent();
        });
        return app;
    }

    private static async Task<ContextoAvisos> ContextoAsync(Guid? sucursalId, ICurrentUserContext user,
        ICurrentEmpresaContext empresa, IdentidadDbContext identidad, IUsuarioSucursalReadPort sucursales,
        ICurrentUserPermissions permisos, CancellationToken ct)
    {
        var uid = user.UserId ?? throw new UnauthorizedAccessException("Se requiere usuario autenticado.");
        var eid = empresa.Current ?? throw new ForbiddenException("EMPRESA_REQUERIDA", "Selecciona una empresa.");
        if (!await identidad.Usuarios.AnyAsync(x => x.Id == uid && x.Activo, ct) ||
            !await identidad.UsuarioEmpresaRoles.AnyAsync(x => x.UsuarioId == uid && x.EmpresaId == eid, ct))
            throw new ForbiddenException("AVISOS_SIN_ACCESO", "No tienes acceso a esta empresa.");
        if (sucursalId is { } sid)
        {
            if (sid == Guid.Empty) throw new BusinessRuleException("AVISOS_SUCURSAL_INVALIDA", "Selecciona una sucursal válida.");
            var asociada = await sucursales.EstaAsociadoAsync(uid, sid, ct) || await sucursales.EsCorporativoAsync(uid, ct);
            if (!asociada)
            {
                foreach (var permiso in new[] { "compras.requisiciones.leer-todas-sucursales", "compras.ordenes.leer-todas-sucursales",
                    "almacen.salidas.leer-todas-sucursales", "almacen.salidas.gestionar-todas-sucursales",
                    "almacen.inventarios.leer-todas-sucursales" })
                    if (await permisos.TieneAsync(permiso, ct)) { asociada = true; break; }
            }
            if (!asociada) throw new ForbiddenException("SUCURSAL_SIN_ACCESO", "No tienes acceso a esta sucursal.");
        }
        return new ContextoAvisos(uid, eid, sucursalId);
    }
}

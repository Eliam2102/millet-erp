using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Api.Auth.Models;
using Millet.Identidad.Application;
using Millet.SharedKernel.Application;

namespace Millet.Api.Auth;

/// <summary>
/// Endpoints de auth reales (modo <c>EntraId</c> y compatible con
/// <c>FakeForLocalDev</c> en cuanto haya un token compatible).
/// <list type="bullet">
///   <item><c>POST /api/auth/sesion</c> — intercambia un token de Entra por
///         un JWT del API. Anonymous (es el punto de entrada).</item>
///   <item><c>POST /api/auth/cambiar-empresa</c> — re-emite el JWT con un
///         <c>current_empresa_id</c> distinto (el usuario debe tener
///         asignación a la empresa solicitada). Autenticado.</item>
///   <item><c>GET /api/auth/me</c> — devuelve los datos del usuario activo
///         desde claims, sin tocar BD. Requiere autenticación.</item>
/// </list>
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/sesion", async (
            [FromBody] LoginRequest request,
            LoginOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            var response = await orchestrator.LoginWithEntraTokenAsync(
                request.EntraToken,
                request.EmpresaId,
                cancellationToken);

            return Results.Ok(response);
        })
        .AllowAnonymous()
        .WithName("PostSesion");

        group.MapPost("/cambiar-empresa", async (
            [FromBody] CambiarEmpresaRequest request,
            LoginOrchestrator orchestrator,
            ICurrentUserContext currentUser,
            CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is not Guid userId)
            {
                return Results.Unauthorized();
            }

            var response = await orchestrator.ChangeEmpresaAsync(
                userId,
                request.EmpresaId,
                cancellationToken);

            return Results.Ok(response);
        })
        .RequireAuthorization()
        .WithName("PostCambiarEmpresa");

        group.MapGet("/sucursales", async (
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            ICurrentUserPermissions permisos,
            Millet.Identidad.Infrastructure.IdentidadDbContext identidadDb,
            CancellationToken ct) =>
        {
            if (currentUser.UserId is not Guid usuarioId || currentEmpresa.Current is null)
                return Results.Ok(Array.Empty<SucursalSesionResponse>());
            if (!await identidadDb.Usuarios.AsNoTracking().AnyAsync(u => u.Id == usuarioId && u.Activo, ct))
                return Results.Forbid();

            IQueryable<Sucursal> query = identidadDb.Set<Sucursal>().AsNoTracking()
                .Where(s => s.Estatus == EstatusCatalogo.Activo);
            var puedeVerTodas = await permisos.TieneAsync(
                Millet.Identidad.Domain.PermisosCanonicos.AdminSucursalesUsuariosGestionar, ct);
            if (!puedeVerTodas)
            {
                query = query.Where(s => identidadDb.UsuarioSucursales.Any(a =>
                    a.UsuarioId == usuarioId && a.SucursalId == s.Id &&
                    a.Estatus == EstatusCatalogo.Activo));
            }

            var items = await query.OrderBy(s => s.Nombre)
                .Select(s => new SucursalSesionResponse(s.Id, s.Clave, s.Nombre))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization()
        .WithName("GetSucursalesDeSesion")
        .WithSummary("Sucursales operativas permitidas en la empresa actual")
        .Produces<IReadOnlyList<SucursalSesionResponse>>(StatusCodes.Status200OK);

        group.MapGet("/me", async (
            ICurrentUserContext currentUser,
            ICurrentEmpresaContext currentEmpresa,
            IPermissionCache permissionCache,
            IPermissionLoader permissionLoader,
            Millet.Identidad.Infrastructure.IdentidadDbContext identidadDb,
            Millet.Compras.Infrastructure.ComprasDbContext comprasDb,
            CancellationToken cancellationToken) =>
        {
            if (currentUser.UserId is not Guid userId)
            {
                return Results.Unauthorized();
            }

            // Permisos efectivos del usuario en la empresa actual. Se leen
            // de la caché (TTL 5 min, ADR-0007); en miss se cargan desde BD
            // y se cachean. Si no hay empresa seleccionada, lista vacía.
            IReadOnlyList<string> permisos = Array.Empty<string>();
            Millet.Compras.Application.Settings.ComprasSettingsResponse? comprasSettings = null;
            if (currentEmpresa.Current is Guid empresaId)
            {
                var cached = await permissionCache.GetAsync(userId, empresaId, cancellationToken);
                if (cached is null)
                {
                    var fresh = await permissionLoader.LoadForUserInEmpresaAsync(
                        userId, empresaId, cancellationToken);
                    await permissionCache.SetAsync(userId, empresaId, fresh, cancellationToken);
                    permisos = fresh.ToList();
                }
                else
                {
                    permisos = cached.ToList();
                }

                // Compras settings de la empresa actual. Single SELECT, ~5ms;
                // si la fila no existe (empresa nueva sin personalizar) se
                // entrega el default (false). Encaja con Q2-a — el FE
                // recibe la config en la sesión, sin fetch extra.
                var settingsRow = await comprasDb.ComprasSettings
                    .AsNoTracking()
                    .Where(s => s.EmpresaId == empresaId)
                    .Select(s => new { s.EmpresaId, s.AutoGenerarOcAlAutorizar })
                    .FirstOrDefaultAsync(cancellationToken);
                comprasSettings = new Millet.Compras.Application.Settings.ComprasSettingsResponse(
                    EmpresaId: empresaId,
                    AutoGenerarOcAlAutorizar: settingsRow?.AutoGenerarOcAlAutorizar ?? false);
            }

            // B.1: traer DepartamentoId del usuario para que el FE filtre
            // su bandeja por defecto. Single SELECT por id.
            var departamentoId = await identidadDb.Usuarios
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.DepartamentoId)
                .FirstOrDefaultAsync(cancellationToken);

            return Results.Ok(new MeResponse(
                UserId: userId,
                Email: string.Empty, // El JWT no incluye email en current user context; PR siguiente lo agrega si se necesita
                Nombre: currentUser.UserName ?? string.Empty,
                CurrentEmpresaId: currentEmpresa.Current,
                DepartamentoId: departamentoId,
                Permisos: permisos,
                ComprasSettings: comprasSettings));
        })
        .RequireAuthorization()
        .WithName("GetMe");

        return app;
    }
}

public sealed record SucursalSesionResponse(Guid Id, string Clave, string Nombre);

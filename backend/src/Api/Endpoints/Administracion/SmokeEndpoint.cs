using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Smoke endpoint del andamio del área Administración (F-Admin-PR1.2).
/// Existe únicamente para validar end-to-end la pieza nueva:
///
/// <list type="bullet">
///   <item>Permiso canónico <c>admin.empresas.leer</c> seedeado en
///         <c>identidad.permisos</c> (migración <c>AdminPermisosCanonicos</c>).</item>
///   <item>El attribute <see cref="RequirePermissionAttribute"/> bloquea
///         con 403 cuando el usuario no tiene el permiso.</item>
///   <item>El attribute permite con 200 cuando sí lo tiene (super-admin
///         vía bootstrap).</item>
/// </list>
///
/// Cuando F-Admin-PR2 (Empresas) llegue con endpoints reales bajo
/// <c>/api/v1/admin/empresas</c>, este smoke puede deprecarse. Se deja
/// activo en MVP para que el frontend del andamio (UF-Admin-PR1) tenga
/// un endpoint contra el cual validar el wiring de auth y permisos
/// canónicos sin depender de features posteriores.
/// </summary>
public static class SmokeEndpoint
{
    public static IEndpointRouteBuilder MapAdminSmokeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/smoke", () => Results.Ok(new { ok = true }))
            .RequireAuthorization(
                PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpresasLeer)
            .WithTags("Administracion")
            .WithName("AdminSmoke")
            .WithSummary("Smoke del andamio del área Administración (F-Admin-PR1.2)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

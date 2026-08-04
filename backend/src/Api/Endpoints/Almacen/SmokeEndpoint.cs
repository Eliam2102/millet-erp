using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Smoke endpoint del módulo Almacén (F0-PR1). Existe únicamente para
/// validar end-to-end la fundación del módulo:
///
/// <list type="bullet">
///   <item>Permiso canónico <c>almacen.almacenes.read</c> seedeado en
///         <c>identidad.permisos</c> (migración <c>AlmacenPermisosCanonicos</c>).</item>
///   <item>El attribute <see cref="RequirePermissionAttribute"/> bloquea
///         con 403 cuando el usuario no tiene el permiso.</item>
///   <item>El attribute permite con 200 cuando sí lo tiene (super-admin
///         vía bootstrap).</item>
///   <item><c>AlmacenDbContext</c> está registrado y sus migraciones
///         aplicadas (verificable vía <c>/health/ready</c>).</item>
/// </list>
///
/// Cuando F1 (catálogo) llegue con endpoints reales bajo
/// <c>/api/v1/almacen/almacenes</c>, este smoke puede deprecarse.
/// </summary>
public static class SmokeEndpoint
{
    public static IEndpointRouteBuilder MapAlmacenSmokeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/almacen/smoke", () => Results.Ok(new { ok = true }))
            .RequireAuthorization(
                PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesRead)
            .WithTags("Almacen")
            .WithName("AlmacenSmoke")
            .WithSummary("Smoke del módulo Almacén (F0-PR1)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Smoke endpoint del módulo Facturación (F0-PR1). Existe únicamente para
/// validar end-to-end la fundación del módulo:
///
/// <list type="bullet">
///   <item>Permiso canónico <c>facturacion.facturas.leer</c> seedeado en
///         <c>identidad.permisos</c> (migración <c>FacturacionPermisosCanonicos</c>).</item>
///   <item>El attribute <see cref="RequirePermissionAttribute"/> bloquea con
///         403 cuando el usuario no tiene el permiso y permite con 200 cuando
///         sí lo tiene (super-admin vía bootstrap).</item>
///   <item><c>FacturacionDbContext</c> está registrado y sus migraciones
///         aplicadas (verificable vía <c>/health/ready</c>).</item>
/// </list>
///
/// Cuando F1 llegue con endpoints reales bajo <c>/api/v1/facturacion/facturas</c>
/// y <c>/pedidos-facturables</c>, este smoke puede deprecarse.
/// </summary>
public static class SmokeEndpoint
{
    public static IEndpointRouteBuilder MapFacturacionSmokeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/facturacion/smoke", () => Results.Ok(new { ok = true }))
            .RequireAuthorization(
                PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
            .WithTags("Facturacion")
            .WithName("FacturacionSmoke")
            .WithSummary("Smoke del módulo Facturación (F0-PR1)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}

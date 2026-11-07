using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// H1 paso 2 (86e3z5w25): área y equipo de los gastos de caja chica, viáticos y tarjeta. Quien captura no tiene permisos de
/// Contabilidad: estas lecturas se autorizan con los permisos de captura de CxP y Contabilidad resuelve las exigencias C-03 y los
/// centros de la sucursal con la que se contabilizará el gasto (la de la comprobación de caja chica; la del empleado en viáticos y
/// tarjeta).
/// </summary>
public static class GastosDimensionesEndpoints
{
    private const string Captura =
        PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarComprobacionesCapturar + "|"
        + PermisosCanonicos.CuentasPorPagarViaticosCapturarComprobacion + "|" + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento;

    public static IEndpointRouteBuilder MapGastosDimensionesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cuentas-por-pagar/gastos").WithTags("CuentasPorPagar").RequireAuthorization();

        group.MapGet("/dimensiones", async (
            [FromQuery] Guid? sucursalId, [FromQuery] Guid? empleadoId,
            ICurrentEmpresaContext empresa, IEmpleadoReadPort empleados, IDimensionesGastoPort dimensiones, IClock clock,
            CancellationToken ct) =>
        {
            if (empresa.Current is not { } empresaId) return Results.Forbid();
            var sucursal = await SucursalAsync(sucursalId, empleadoId, empleados, ct);
            if (sucursal is null)
                return Results.Ok(new DimensionesGastoResponse(null, false, false, null));
            var e = await dimensiones.ExigenciasAsync(empresaId, sucursal.Value, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), ct);
            return Results.Ok(new DimensionesGastoResponse(sucursal, e.ExigeArea, e.ExigeEquipo, e.CuentaCodigo));
        })
        .RequireAuthorization(Captura)
        .WithName("DimensionesGastoCxp")
        .WithSummary("Sucursal con la que se contabiliza el gasto y si su cuenta exige área o equipo (regla C-03)")
        .Produces<DimensionesGastoResponse>();

        group.MapGet("/centros", async (
            [FromQuery] Guid sucursalId, [FromQuery] int? nivel, [FromQuery] Guid? dim2Id, [FromQuery] string? q, [FromQuery] int? limit,
            ICurrentEmpresaContext empresa, IDimensionesGastoPort dimensiones, CancellationToken ct) =>
        {
            if (empresa.Current is not { } empresaId) return Results.Forbid();
            return Results.Ok(await dimensiones.CentrosAsync(empresaId, sucursalId, nivel ?? 2, dim2Id, q, limit ?? 50, ct));
        })
        .RequireAuthorization(Captura)
        .WithName("CentrosGastoCxp")
        .WithSummary("Áreas (nivel 2) o equipos (nivel 3) activos de la sucursal del gasto")
        .Produces<IReadOnlyList<CentroGastoOpcion>>();

        return app;
    }

    private static async Task<Guid?> SucursalAsync(Guid? sucursalId, Guid? empleadoId, IEmpleadoReadPort empleados, CancellationToken ct)
    {
        if (sucursalId is { } s && s != Guid.Empty) return s;
        if (empleadoId is not { } e || e == Guid.Empty) return null;
        // En tarjeta el id viene del selector de usuarios: si no es de un empleado, el empleado vinculado a ese usuario.
        var empleado = await empleados.ObtenerAsync(e, ct) ?? await empleados.ObtenerPorUsuarioAsync(e, ct);
        return empleado?.SucursalId is { } suc && suc != Guid.Empty ? suc : null;
    }

    /// <param name="SucursalId">Null si no se pudo determinar (empleado sin sucursal): no se ofrecen centros ni se exige nada.</param>
    public sealed record DimensionesGastoResponse(Guid? SucursalId, bool ExigeArea, bool ExigeEquipo, string? CuentaCodigo);
}

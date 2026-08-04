using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Catalogos;

/// <summary>
/// Endpoints HTTP read-only para catálogos SAT cross-empresa
/// (F-Admin-PR5.3):
/// <list type="bullet">
///   <item><c>GET /api/v1/catalogos/formas-pago</c> — c_FormaPago SAT (22 entries).</item>
///   <item><c>GET /api/v1/catalogos/usos-cfdi</c> — c_UsoCFDI SAT (23 entries comunes).</item>
/// </list>
///
/// <para>
/// Read-mostly: actualizaciones via migraciones aditivas cuando SAT
/// publica versiones nuevas. Sin CRUD UI. Permiso:
/// <c>compartido.catalogos.leer</c>. Sin paginación (catálogos
/// pequeños y fijos). <c>regimenes-fiscales</c> ya existe en
/// <see cref="CatalogosOcEndpoints"/>.
/// </para>
/// </summary>
public static class CatalogosSatEndpoints
{
    public static IEndpointRouteBuilder MapCatalogosSatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos").WithTags("Catalogos");

        group.MapGet("/formas-pago", async (
            CompartidoDbContext db, CancellationToken ct) =>
        {
            var items = await db.FormasPago.AsNoTracking()
                .Where(f => f.Activa)
                .OrderBy(f => f.ClaveSat)
                .Select(f => new FormaPagoItem(f.Id, f.ClaveSat, f.Descripcion, f.Activa))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarFormasPago")
        .WithSummary("Catálogo SAT de formas de pago (F-Admin-PR5.3)")
        .Produces<IReadOnlyList<FormaPagoItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/usos-cfdi", async (
            CompartidoDbContext db, CancellationToken ct) =>
        {
            var items = await db.UsosCfdi.AsNoTracking()
                .Where(u => u.Activa)
                .OrderBy(u => u.ClaveSat)
                .Select(u => new UsoCfdiItem(u.Id, u.ClaveSat, u.Descripcion, u.AplicaTipoPersona, u.Activa))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarUsosCfdi")
        .WithSummary("Catálogo SAT de usos CFDI (F-Admin-PR5.3)")
        .Produces<IReadOnlyList<UsoCfdiItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    public sealed record FormaPagoItem(Guid Id, string ClaveSat, string Descripcion, bool Activa);
    public sealed record UsoCfdiItem(
        Guid Id, string ClaveSat, string Descripcion,
        AplicaTipoPersona AplicaTipoPersona, bool Activa);
}

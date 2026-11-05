using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
namespace Millet.Api.Endpoints.ComercioExterior;
public static class ConfiguracionCeEndpoints
{
    public sealed record PlazoDatos(int DiasHabilesIncidenciaRecepcion);
    public static void MapConfiguracionCe(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/comercio-exterior/configuracion").RequireAuthorization();
        g.MapGet("/", async (ComercioExteriorDbContext db, CancellationToken ct) =>
            Results.Ok(new PlazoDatos(await db.Set<ConfiguracionCe>().Select(c => (int?)c.DiasHabilesIncidenciaRecepcion).SingleOrDefaultAsync(ct) ?? 2)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComercioExteriorExpedientesLeer);
        g.MapPut("/", async (PlazoDatos datos, ComercioExteriorDbContext db, ICurrentEmpresaContext empresa, CancellationToken ct) =>
        {
            var c = await db.Set<ConfiguracionCe>().SingleOrDefaultAsync(ct);
            if (c is null) { c = new(empresa.Current ?? throw new UnauthorizedAccessException()); db.Add(c); }
            c.CambiarPlazo(datos.DiasHabilesIncidenciaRecepcion); await db.SaveChangesAsync(ct);
            return Results.Ok(new PlazoDatos(c.DiasHabilesIncidenciaRecepcion));
        }).RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminParametrosEditar).WithMetadata(new RequireIdempotencyKeyAttribute());
    }
}

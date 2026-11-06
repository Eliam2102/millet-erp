using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Consumo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Catálogo de normas de consumo por artículo componente (A4.8, D3/D4): consumo por unidad de la pieza, % de merma y,
/// para artículos en M³ (argón), el factor m² → m³. Lectura con el permiso de la bandeja de salidas; edición con el
/// de administrar almacenes. Arranca vacío: lo que no tenga norma queda en la bandeja de revisión.
/// </summary>
public static class NormasConsumoEndpoints
{
    public sealed record NormaConsumoRequest(decimal ConsumoPorUnidad, decimal PorcentajeMerma, decimal? FactorM3PorM2);
    public sealed record NormaConsumoResponse(Guid ArticuloId, string? ArticuloClave, string? ArticuloDescripcion, string? UnidadMedida,
        decimal ConsumoPorUnidad, decimal PorcentajeMerma, decimal? FactorM3PorM2, DateTimeOffset UpdatedAt);

    public static IEndpointRouteBuilder MapNormasConsumoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/normas-consumo").WithTags("Almacen");

        group.MapGet("/", async (AlmacenDbContext db, IArticuloReadPort articulos, CancellationToken ct) =>
        {
            var normas = await db.NormasConsumoComponente.AsNoTracking().ToListAsync(ct);
            var maestro = await articulos.ObtenerPorIdsAsync(normas.Select(n => n.ArticuloId).ToList(), ct);
            return Results.Ok(normas.Select(n => Respuesta(n, maestro.GetValueOrDefault(n.ArticuloId)))
                .OrderBy(n => n.ArticuloClave).ToList());
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ListarNormasConsumo")
        .Produces<List<NormaConsumoResponse>>(StatusCodes.Status200OK);

        group.MapPut("/{articuloId:guid}", async (Guid articuloId, NormaConsumoRequest req, AlmacenDbContext db,
            IArticuloReadPort articulos, CancellationToken ct) =>
        {
            var articulo = await articulos.ObtenerAsync(articuloId, ct);
            if (articulo is null) return Results.NotFound();
            var norma = await db.NormasConsumoComponente.FirstOrDefaultAsync(n => n.ArticuloId == articuloId, ct);
            if (norma is null) db.NormasConsumoComponente.Add(norma = new NormaConsumoComponente(articuloId, req.ConsumoPorUnidad, req.PorcentajeMerma, req.FactorM3PorM2));
            else norma.Actualizar(req.ConsumoPorUnidad, req.PorcentajeMerma, req.FactorM3PorM2);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            {
                return Results.Conflict(); // otra captura simultánea del mismo artículo; se vuelve a intentar.
            }
            return Results.Ok(Respuesta(norma, articulo));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenAlmacenesAdministrar)
        .WithName("GuardarNormaConsumo")
        .Produces<NormaConsumoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static NormaConsumoResponse Respuesta(NormaConsumoComponente n, ArticuloLectura? a) =>
        new(n.ArticuloId, a?.Clave, a?.Descripcion, a?.UnidadMedida, n.ConsumoPorUnidad, n.PorcentajeMerma, n.FactorM3PorM2, n.UpdatedAt);
}

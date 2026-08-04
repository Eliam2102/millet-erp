using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Api.Endpoints.Catalogos;

/// <summary>
/// Endpoints HTTP read-only de los catálogos OC del schema
/// <c>compartido</c> (F9-PR1):
/// <list type="bullet">
///   <item><c>GET /api/v1/catalogos/incoterms</c></item>
///   <item><c>GET /api/v1/catalogos/transportistas</c></item>
///   <item><c>GET /api/v1/catalogos/regimenes-fiscales</c></item>
///   <item><c>GET /api/v1/catalogos/condiciones-pago</c></item>
/// </list>
///
/// <para>
/// Permiso: <c>compartido.catalogos.leer</c>. Sin paginación porque
/// los catálogos tienen pocas filas (Incoterms = 11, Regímenes Fiscales
/// = 8, Condiciones de Pago = 7). Transportistas puede crecer; si
/// supera 200 filas se introducirá paginación.
/// </para>
/// </summary>
public static class CatalogosOcEndpoints
{
    public static IEndpointRouteBuilder MapCatalogosOcEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos").WithTags("Catalogos");

        group.MapGet("/incoterms", async (
            [FromQuery] EstatusCatalogo? estatus,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            IQueryable<Incoterm> q = db.Incoterms.AsNoTracking();
            if (estatus is EstatusCatalogo e) q = q.Where(x => x.Estatus == e);
            var items = await q
                .OrderBy(x => x.Codigo)
                .Select(x => new IncotermItem(x.Id, x.Codigo, x.Nombre, x.Estatus))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarIncoterms")
        .WithSummary("Catálogo de Incoterms 2020 (F9-PR1)")
        .Produces<IReadOnlyList<IncotermItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/transportistas", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] string? clave,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            IQueryable<Transportista> q = db.Transportistas.AsNoTracking();
            if (estatus is EstatusCatalogo e) q = q.Where(x => x.Estatus == e);
            if (!string.IsNullOrWhiteSpace(clave)) q = q.Where(x => x.Clave.Contains(clave));
            var items = await q
                .OrderBy(x => x.Clave)
                .Select(x => new TransportistaItem(x.Id, x.Clave, x.Nombre, x.Email, x.Telefono, x.Estatus))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarTransportistas")
        .WithSummary("Catálogo de transportistas (F9-PR1)")
        .Produces<IReadOnlyList<TransportistaItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/regimenes-fiscales", async (
            [FromQuery] EstatusCatalogo? estatus,
            [FromQuery] bool? aplicaPersonaFisica,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            IQueryable<RegimenFiscal> q = db.RegimenesFiscales.AsNoTracking();
            if (estatus is EstatusCatalogo e) q = q.Where(x => x.Estatus == e);
            if (aplicaPersonaFisica is bool apf) q = q.Where(x => x.AplicaPersonaFisica == apf);
            var items = await q
                .OrderBy(x => x.Codigo)
                .Select(x => new RegimenFiscalItem(x.Id, x.Codigo, x.Nombre, x.AplicaPersonaFisica, x.Estatus))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarRegimenesFiscales")
        .WithSummary("Catálogo de regímenes fiscales SAT (F9-PR1)")
        .Produces<IReadOnlyList<RegimenFiscalItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/condiciones-pago", async (
            [FromQuery] EstatusCatalogo? estatus,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            IQueryable<CondicionesPago> q = db.CondicionesPago.AsNoTracking();
            if (estatus is EstatusCatalogo e) q = q.Where(x => x.Estatus == e);
            var items = await q
                .OrderBy(x => x.DiasCredito)
                .Select(x => new CondicionesPagoItem(x.Id, x.Clave, x.Nombre, x.DiasCredito, x.Estatus))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarCondicionesPago")
        .WithSummary("Catálogo de condiciones de pago (F9-PR1)")
        .Produces<IReadOnlyList<CondicionesPagoItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // UF2-PR1: catálogo `usos_principales`. Brecha detectada al
        // wirear el Sheet "Nueva OC" (CrearOrdenCompraVacia requiere
        // UsoPrincipalId pero F9-PR1 no sembró este catálogo). Datos
        // Maestros post-MVP tomará el CRUD.
        group.MapGet("/usos-principales", async (
            [FromQuery] EstatusCatalogo? estatus,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            IQueryable<UsoPrincipal> q = db.UsosPrincipales.AsNoTracking();
            if (estatus is EstatusCatalogo e) q = q.Where(x => x.Estatus == e);
            var items = await q
                .OrderBy(x => x.Nombre)
                .Select(x => new UsoPrincipalItem(x.Id, x.Clave, x.Nombre, x.Estatus))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarUsosPrincipales")
        .WithSummary("Catálogo de usos principales para OC (UF2-PR1)")
        .Produces<IReadOnlyList<UsoPrincipalItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // ADR-0046 Etapa 1a: catálogo de unidades de medida. Read-only aquí
        // (permiso `compartido.catalogos.leer`); el CRUD vive en
        // CatalogosEditablesEndpoints (permiso `catalogos.unidades-medida.gestionar`).
        group.MapGet("/unidades-medida", async (
            [FromQuery] EstatusCatalogo? estatus,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            IQueryable<UnidadMedida> q = db.UnidadesMedida.AsNoTracking();
            if (estatus is EstatusCatalogo e) q = q.Where(x => x.Estatus == e);
            var items = await q
                .OrderBy(x => x.Dimension).ThenBy(x => x.Codigo)
                .Select(x => new UnidadMedidaItem(
                    x.Id, x.Codigo, x.Nombre, x.Dimension, x.FactorABase,
                    x.Decimales, x.EsBase, x.Estatus))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarUnidadesMedida")
        .WithSummary("Catálogo de unidades de medida (ADR-0046)")
        .Produces<IReadOnlyList<UnidadMedidaItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // Patrón catálogo (ADR-0046): categoría de artículo. Read-only aquí
        // (permiso `compartido.catalogos.leer`); el CRUD vive en
        // CatalogosEditablesEndpoints (permiso `compartido.catalogos.administrar`).
        group.MapGet("/categorias-articulo", async (
            [FromQuery] EstatusCatalogo? estatus,
            CompartidoDbContext db,
            CancellationToken ct) =>
        {
            IQueryable<CategoriaArticulo> q = db.CategoriasArticulo.AsNoTracking();
            if (estatus is EstatusCatalogo e) q = q.Where(x => x.Estatus == e);
            var items = await q
                .OrderBy(x => x.Nombre)
                .Select(x => new CategoriaArticuloItem(x.Id, x.Nombre, x.Estatus))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarCategoriasArticulo")
        .WithSummary("Catálogo de categorías de artículo (patrón ADR-0046)")
        .Produces<IReadOnlyList<CategoriaArticuloItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    public sealed record IncotermItem(Guid Id, string Codigo, string Nombre, EstatusCatalogo Estatus);
    public sealed record UnidadMedidaItem(
        Guid Id, string Codigo, string Nombre, DimensionUnidad Dimension,
        decimal FactorABase, int Decimales, bool EsBase, EstatusCatalogo Estatus);
    public sealed record TransportistaItem(Guid Id, string Clave, string Nombre, string? Email, string? Telefono, EstatusCatalogo Estatus);
    public sealed record RegimenFiscalItem(Guid Id, string Codigo, string Nombre, bool AplicaPersonaFisica, EstatusCatalogo Estatus);
    public sealed record CondicionesPagoItem(Guid Id, string Clave, string Nombre, int DiasCredito, EstatusCatalogo Estatus);
    public sealed record UsoPrincipalItem(Guid Id, string Clave, string Nombre, EstatusCatalogo Estatus);
    public sealed record CategoriaArticuloItem(Guid Id, string Nombre, EstatusCatalogo Estatus);
}

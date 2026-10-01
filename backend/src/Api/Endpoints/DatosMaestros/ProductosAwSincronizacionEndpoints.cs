using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Endpoints.CentrosCosto;
using Millet.Api.Web;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.DatosMaestros;

public sealed record ProductoAwSincronizacionDetalle(
    string? Resultado, string? Error, string? Diferencias, string? HashOrigen,
    DateTime? LeidoEnUtc, DateTime? AplicadoEnUtc, string? VersionContrato, string? VersionMapeo,
    string? DescripcionOrigen, string? UnidadOrigenCruda, string? BajaOrigenCruda);

public sealed record ProductoAwSincronizacionEstado(
    Guid ProductoId, string Referencia, int Version, ProductoAwSincronizacionDetalle? Sincronizacion);

/// <summary>
/// ADM-07 D1/D2: sincronización de productos A+W (síncrona y acotada, sin entidad de ejecución) y
/// contrato de lectura. Permiso: <c>datos_maestros.productos-aw.gestionar</c> (el mismo de ADM-01/02;
/// no hay permiso nuevo). Productos es master cross-empresa sin alcance por sucursal (ADR-0048/0051),
/// por eso no se usa SucursalScopeGuard: el control de acceso es el permiso, verificado en la API.
/// </summary>
public static class ProductosAwSincronizacionEndpoints
{
    private const string Base = "/api/v1/datos-maestros/productos-aw";

    public static IEndpointRouteBuilder MapProductosAwSincronizacionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Base).WithTags("DatosMaestros");
        var policy = PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosProductosAwGestionar;

        // Barrido acotado (lote por páginas del origen). Devuelve el resumen real.
        group.MapPost("/sincronizacion", async (AwProductosSincronizador sync, CancellationToken ct) =>
            Results.Ok(await sync.SincronizarBarridoAsync(ct)))
        .RequireAuthorization(policy)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("SincronizarProductosAw")
        .WithSummary("Barrido síncrono de productos A+W (resumen con errores por referencia)")
        .Produces<AwProductosResumen>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Reintento por referencia; If-Match opcional (ADR-0012): versión vieja => 409.
        group.MapPost("/sincronizacion/{referencia}", async (
            string referencia,
            [Microsoft.AspNetCore.Mvc.FromHeader(Name = "If-Match")] string? ifMatch,
            AwProductosSincronizador sync,
            CancellationToken ct) =>
        {
            int? version = null;
            if (!string.IsNullOrWhiteSpace(ifMatch))
            {
                if (!CentrosCostoCatalogoEndpoints.TryParseVersion(ifMatch, out var v))
                    throw new BusinessRuleException("IF_MATCH_INVALIDO", "If-Match debe ser la versión (ETag) del producto.");
                version = v;
            }
            var resumen = await sync.SincronizarReferenciaAsync(referencia, ct, version);
            if (resumen.ErroresPorReferencia.Any(e => e.Codigo == "no_encontrada_en_origen"))
                throw new EntityNotFoundException("PRODUCTO_AW_ORIGEN_NO_ENCONTRADO", "La referencia no existe en el origen A+W.");
            if (resumen.Conflictos > 0)
                throw new ConflictException("PRODUCTO_AW_CONFLICTO_VERSION",
                    "El producto cambió o es manual; no se sobrescribió. Relee y reintenta.");
            return Results.Ok(resumen);
        })
        .RequireAuthorization(policy)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReintentarProductoAw")
        .WithSummary("Relee una referencia del origen y la aplica (inline; If-Match opcional)")
        .Produces<AwProductosResumen>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{id:guid}/sincronizacion", async (
            Guid id, HttpResponse response, CompartidoDbContext db, CancellationToken ct) =>
        {
            var p = await db.ProductosAw.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.Id, x.ReferenciaExterna, x.Version }).FirstOrDefaultAsync(ct)
                ?? throw new EntityNotFoundException("PRODUCTO_AW_NO_ENCONTRADO", "Producto A+W no encontrado.");
            var r = await db.ProductosSincronizacionAw.AsNoTracking().Where(x => x.ProductoAwId == id)
                .Select(x => new ProductoAwSincronizacionDetalle(
                    x.Resultado.ToString(), x.Error, x.Diferencias, x.HashOrigen, x.LeidoEnUtc, x.AplicadoEnUtc,
                    x.VersionContrato, x.VersionMapeo, x.DescripcionOrigen, x.UnidadOrigenCruda, x.BajaOrigenCruda))
                .FirstOrDefaultAsync(ct);
            CentrosCostoCatalogoEndpoints.SetEtag(response, p.Version);
            return Results.Ok(new ProductoAwSincronizacionEstado(p.Id, p.ReferenciaExterna, p.Version, r));
        })
        .RequireAuthorization(policy)
        .WithName("ObtenerSincronizacionProductoAw")
        .WithSummary("Estado de sincronización del producto (resultado, error, diferencias, hash, leído/aplicado; ETag)")
        .Produces<ProductoAwSincronizacionEstado>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{referencia}/contrato", async (
            string referencia, HttpResponse response, IProductoAwContratoReadPort port, CancellationToken ct) =>
        {
            var c = await port.ObtenerPorReferenciaAsync(referencia, ct)
                ?? throw new EntityNotFoundException("PRODUCTO_AW_NO_ENCONTRADO", "Producto A+W no encontrado.");
            CentrosCostoCatalogoEndpoints.SetEtag(response, c.Version);
            return Results.Ok(c);
        })
        .RequireAuthorization(policy)
        .WithName("ObtenerContratoProductoAw")
        .WithSummary("Contrato de lectura estable ProductoAwContratoV1 por referencia (incluye inactivos)")
        .Produces<ProductoAwContratoV1>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}

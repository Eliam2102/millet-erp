using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Compras.Oc;

/// <summary>
/// Endpoint read-only del catálogo <c>compras.tipos_documento_oc</c>
/// (UF3-PR2). Usado por <c>&lt;TipoDocumentoSelector/&gt;</c> en el
/// <c>&lt;AdjuntosManager/&gt;</c> del Tab "Adjuntos" del detalle de OC.
///
/// <para>Permiso: <c>compras.ordenes.adjuntar</c>. Solo activos por
/// default; sin paginación porque son 7 tipos seedeados (C6 §4.12).
/// El frontend cachea 1h porque el catálogo es casi inmutable.</para>
///
/// <para>Cross-módulo: este catálogo es OC-specific. CxP/Activos
/// tendrán sus propios catálogos (<c>cxp.tipos_documento</c>,
/// <c>activos.tipos_documento</c>) cuando llegue su fase. El selector
/// del frontend (<c>TipoDocumentoSelector</c>) es genérico y acepta
/// items como prop, así que cada módulo lo wirea con su catálogo.</para>
/// </summary>
public static class TiposDocumentoOcEndpoint
{
    public static IEndpointRouteBuilder MapTiposDocumentoOcEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/compras/catalogos/tipos-documento-oc", async (
            ComprasDbContext db,
            CancellationToken ct,
            bool? includeInactivas) =>
        {
            IQueryable<TipoDocumentoOc> q = db.TiposDocumentoOc.AsNoTracking();
            if (includeInactivas is not true)
            {
                q = q.Where(t => t.Activo);
            }
            var items = await q
                .OrderBy(t => t.Clave)
                .Select(t => new TipoDocumentoOcItem(
                    t.Id,
                    t.Clave,
                    t.Descripcion,
                    t.ObligatorioSiImportacion,
                    t.Activo))
                .ToListAsync(ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasOrdenesAdjuntar)
        .WithName("ListarTiposDocumentoOc")
        .WithTags("Compras Catalogos")
        .WithSummary("Catálogo de tipos de documento adjunto de OC (UF3-PR2)")
        .Produces<IReadOnlyList<TipoDocumentoOcItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    public sealed record TipoDocumentoOcItem(
        Guid Id,
        string Clave,
        string Descripcion,
        bool ObligatorioSiImportacion,
        bool Activo);
}

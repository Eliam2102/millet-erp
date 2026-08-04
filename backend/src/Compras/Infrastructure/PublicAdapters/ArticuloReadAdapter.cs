using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="IArticuloReadPort"/> de Compras
/// (ADR-0042). Resuelve <c>articuloId → (clave, nombre)</c> leyendo
/// <c>compartido.articulos</c> via <see cref="CompartidoDbContext"/>.
///
/// <para>
/// Vive en <c>Compras.Infrastructure</c> (el consumidor) y lee del DbContext
/// del data-owner; no hay ciclo (Compartido no referencia Compras), mismo
/// patrón que <see cref="UsuarioReadAdapter"/>.
/// </para>
///
/// <para>
/// <c>AsNoTracking</c> + <c>ICurrentEmpresaContext.Bypass()</c>: el catálogo
/// es cross-empresa (la entidad no implementa <c>IPerteneceAEmpresa</c>). No
/// requiere <c>IgnoreQueryFilters()</c>: <c>Articulo</c> solo implementa
/// <c>IAuditable</c> (no <c>IFiscalmenteRelevante</c>), así que
/// <c>BaseDbContext</c> no le aplica filtro global de soft-delete — un
/// artículo desactivado/borrado igual resuelve su nombre en req/OC históricas.
/// </para>
/// </summary>
public sealed class ArticuloReadAdapter : IArticuloReadPort
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ArticuloReadAdapter(
        CompartidoDbContext db,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
        IReadOnlyCollection<Guid> articuloIds,
        CancellationToken cancellationToken)
    {
        if (articuloIds.Count == 0)
        {
            return new Dictionary<Guid, ArticuloLectura>();
        }

        var distinct = articuloIds.Distinct().ToArray();

        using var bypass = _empresaContext.Bypass();

        return await _db.Articulos
            .AsNoTracking()
            .Where(a => distinct.Contains(a.Id))
            .Select(a => new ArticuloLectura(
                a.Id,
                a.Clave,
                a.Nombre,
                // GAP-9: los servicios no se reciben en Almacén — la
                // naturaleza del artículo alimenta LineaOrdenCompra.EsServicio.
                a.Naturaleza == Naturaleza.Servicio))
            .ToDictionaryAsync(a => a.Id, cancellationToken);
    }
}

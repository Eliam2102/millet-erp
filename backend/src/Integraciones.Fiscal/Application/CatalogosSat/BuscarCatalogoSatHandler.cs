using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Application.CatalogosSat;

/// <summary>
/// Resuelve la búsqueda de catálogo SAT con caché en memoria (los
/// catálogos SAT cambian pocas veces al año — TTL 12 h evita golpear
/// FiscalAPI por cada tecleo del combobox).
///
/// <para>Estrategia según longitud del texto:</para>
/// <list type="bullet">
///   <item><b>1–3 caracteres</b>: lookup exacto por código
///   (<c>GetRecordById</c>) — el PAC exige mínimo 4 para búsqueda por
///   texto, pero claves como <c>MTK</c>/<c>H87</c>/<c>01</c> son más
///   cortas.</item>
///   <item><b>≥4 caracteres</b>: búsqueda por texto (código o
///   descripción).</item>
///   <item><b>Vacío</b> (solo ObjetoImp): busca "impuesto" — todas las
///   descripciones de c_ObjetoImp lo contienen, así que equivale a
///   listar el catálogo completo. VALIDAR en sandbox (FAC-DET-PR1).</item>
/// </list>
/// </summary>
public sealed class BuscarCatalogoSatHandler
    : IRequestHandler<BuscarCatalogoSatQuery, IReadOnlyList<CatalogoSatItem>>
{
    /// <summary>Mínimo del PAC para búsqueda por texto (SDK CatalogService).</summary>
    private const int MinCaracteresBusquedaTexto = 4;

    private const string BusquedaObjetoImpDefault = "impuesto";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    private readonly ISatCatalogosSearchPort _catalogos;
    private readonly ICurrentEmpresaContext _empresa;
    private readonly IMemoryCache _cache;

    public BuscarCatalogoSatHandler(
        ISatCatalogosSearchPort catalogos,
        ICurrentEmpresaContext empresa,
        IMemoryCache cache)
    {
        _catalogos = catalogos;
        _empresa = empresa;
        _cache = cache;
    }

    public async Task<IReadOnlyList<CatalogoSatItem>> Handle(
        BuscarCatalogoSatQuery query, CancellationToken cancellationToken)
    {
        if (_empresa.Current is not Guid empresaId)
            throw new ForbiddenException("EMPRESA_NO_SELECCIONADA",
                "No hay empresa seleccionada en el contexto del request.");

        var buscar = query.Buscar.Trim();
        if (buscar.Length == 0 && query.Catalogo == CatalogoSat.ObjetoImp)
            buscar = BusquedaObjetoImpDefault;

        // El dato es global del SAT (no por empresa) — la key de caché
        // no incluye empresaId a propósito: cualquier credencial válida
        // devuelve el mismo catálogo.
        var cacheKey = $"satcat:{query.Catalogo}:{buscar.ToLowerInvariant()}:{query.Limit}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<CatalogoSatItem>? cached) && cached is not null)
            return cached;

        IReadOnlyList<CatalogoSatItem> items;
        if (buscar.Length < MinCaracteresBusquedaTexto)
        {
            // Las claves de c_ClaveProdServ (8 dígitos) y c_FraccionArancelaria
            // (8-10 dígitos) son largas: con 1–3 caracteres el lookup exacto no
            // puede matchear — el operador va a media palabra. Se devuelve
            // vacío sin llamar al PAC (FAC-DET-PR8: cada tecleo generaba un
            // GET con HTTP 400 garantizado). ClaveUnidad (MTK/H87), ObjetoImp
            // (01–08) y UnidadAduana (06/…) sí tienen códigos cortos y
            // conservan el lookup exacto.
            if (query.Catalogo is CatalogoSat.ClaveProdServ or CatalogoSat.FraccionArancelaria)
                return [];

            var item = await _catalogos.ObtenerPorCodigoAsync(
                empresaId, query.Catalogo, buscar.ToUpperInvariant(), cancellationToken);
            items = item is null ? [] : [item];
        }
        else
        {
            items = await _catalogos.BuscarAsync(
                empresaId, query.Catalogo, buscar, query.Limit, cancellationToken);
        }

        // No cachear misses de código exacto: el operador suele estar a
        // media captura ("MT" → "MTK") y un miss cacheado 12 h taparía
        // el resultado correcto de catálogos que sí cambian.
        if (items.Count > 0)
            _cache.Set(cacheKey, items, CacheTtl);

        return items;
    }
}

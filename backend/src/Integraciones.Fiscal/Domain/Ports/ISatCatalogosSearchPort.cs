namespace Millet.Integraciones.Fiscal.Domain.Ports;

/// <summary>
/// Catálogos SAT consultables en vivo vía FiscalAPI (FAC-DET-PR1).
/// El nombre remoto de cada catálogo vive en
/// <c>FiscalApiSdkAdapterOptions</c> (defaults <c>SatProductCodes</c>,
/// <c>SatUnitMeasurements</c>, <c>SatTaxObjects</c>).
/// </summary>
public enum CatalogoSat
{
    /// <summary>c_ClaveProdServ (~50k claves de producto/servicio).</summary>
    ClaveProdServ = 1,

    /// <summary>c_ClaveUnidad (unidades de medida SAT).</summary>
    ClaveUnidad = 2,

    /// <summary>c_ObjetoImp (objeto de impuesto, 01–08).</summary>
    ObjetoImp = 3,

    /// <summary>c_FraccionArancelaria (fracción arancelaria CCE, 8-10 dígitos).</summary>
    FraccionArancelaria = 4,

    /// <summary>c_UnidadAduana (unidad de medida aduanera CCE, p.ej. 06 = kg).</summary>
    UnidadAduana = 5,

    /// <summary>c_Pais (país de residencia del receptor CCE, ISO alfa-3: USA, CAN…).</summary>
    Pais = 6,

    /// <summary>c_ClavePedimento (clave de pedimento aduanal CCE, p.ej. A1).</summary>
    ClavePedimento = 7,
}

/// <summary>Entrada de un catálogo SAT: código + descripción.</summary>
public sealed record CatalogoSatItem(string Codigo, string Descripcion);

/// <summary>
/// Puerto de búsqueda de catálogos SAT contra el PAC (FiscalAPI). Los
/// catálogos son datos globales del SAT — la empresa solo aporta las
/// credenciales con las que se consulta.
///
/// <para>
/// Restricción del PAC: la búsqueda por texto requiere <b>mínimo 4
/// caracteres</b>; para códigos cortos exactos (p.ej. <c>MTK</c>,
/// <c>H87</c>, <c>01</c>) usar <see cref="ObtenerPorCodigoAsync"/>.
/// </para>
/// </summary>
public interface ISatCatalogosSearchPort
{
    /// <summary>
    /// Busca por texto libre (código o descripción). Lanza
    /// <see cref="Exceptions.CatalogoSatNoDisponibleException"/> si el
    /// PAC no está disponible (SDK deshabilitado, sin credenciales, o
    /// error remoto).
    /// </summary>
    Task<IReadOnlyList<CatalogoSatItem>> BuscarAsync(
        Guid empresaId,
        CatalogoSat catalogo,
        string searchText,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lookup exacto por código (clave SAT). Devuelve <c>null</c> si el
    /// código no existe en el catálogo.
    /// </summary>
    Task<CatalogoSatItem?> ObtenerPorCodigoAsync(
        Guid empresaId,
        CatalogoSat catalogo,
        string codigo,
        CancellationToken cancellationToken);
}

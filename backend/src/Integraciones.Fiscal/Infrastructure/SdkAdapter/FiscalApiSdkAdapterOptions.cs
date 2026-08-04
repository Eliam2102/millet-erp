namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// Configuración global del wrapper del SDK FiscalAPI. Apuntan a una
/// sola cuenta FiscalAPI ("tenant") que aloja múltiples
/// <c>Person</c>s — uno por empresa Millet.
///
/// <para>
/// <b>Por qué TenantKey es global y ApiKey es por-empresa</b>: el
/// modelo de FiscalAPI permite emitir múltiples Api Keys para un mismo
/// Tenant. Las api keys se pueden rotar o revocar individualmente;
/// usamos una por empresa Millet para tener pista de auditoría
/// granular. Pero el Tenant es uno (toda la organización Millet vive
/// bajo un único contrato comercial con FiscalAPI).
/// </para>
///
/// <para>
/// En QA/Prod estos vienen de Key Vault. En dev local pueden venir de
/// <c>appsettings.Development.json</c> o env var:
/// <code>
/// IntegracionesFiscal:Sdk:TenantKey
/// IntegracionesFiscal:Sdk:BaseUrl
/// </code>
/// </para>
/// </summary>
public sealed class FiscalApiSdkAdapterOptions
{
    public const string SectionName = "IntegracionesFiscal:Sdk";

    /// <summary>
    /// X-TENANT-KEY a nivel cuenta. Si vacío, el adapter funciona en
    /// modo NoOp (loggea warning y retorna respuestas vacías).
    /// </summary>
    public string TenantKey { get; set; } = string.Empty;

    /// <summary>
    /// URL base del PAC. Default <c>https://live.fiscalapi.com</c> — ver
    /// doc 02 §13.5: sandbox no soporta el flujo completo de descarga
    /// masiva, así que todos los ambientes Millet apuntan a live.
    /// </summary>
    public string BaseUrl { get; set; } = "https://live.fiscalapi.com";

    /// <summary>API version path segment (default "v4").</summary>
    public string ApiVersion { get; set; } = "v4";

    /// <summary>
    /// Zona horaria que FiscalAPI usa para interpretar fechas en el body
    /// (header <c>X-TIME-ZONE</c>). Default America/Mexico_City — ver doc
    /// 02 §13.4 (CFDI 4.0 error 401 fuera de rango si la fecha viene en
    /// otra TZ).
    /// </summary>
    public string TimeZone { get; set; } = "America/Mexico_City";

    /// <summary>
    /// Si <c>true</c>, el adapter NO instancia el SDK y devuelve
    /// respuestas NoOp (útil en tests / cuando faltan credenciales).
    /// </summary>
    public bool Disabled { get; set; }

    // ── Nombres remotos de catálogos SAT (FAC-DET-PR1) ──────────────────
    // Confirmados contra la lista pública de FiscalAPI
    // (GET /api/v4/catalogs → docs.fiscalapi.com/catalogs). Configurables
    // por si el PAC los renombra; VALIDAR en sandbox con
    // sdk.Catalogs.GetListAsync() antes de encender en dev.

    /// <summary>Catálogo remoto de c_ClaveProdServ.</summary>
    public string CatalogoClaveProdServ { get; set; } = "SatProductCodes";

    /// <summary>Catálogo remoto de c_ClaveUnidad.</summary>
    public string CatalogoClaveUnidad { get; set; } = "SatUnitMeasurements";

    /// <summary>Catálogo remoto de c_ObjetoImp.</summary>
    public string CatalogoObjetoImp { get; set; } = "SatTaxObjects";

    /// <summary>Catálogo remoto de c_FraccionArancelaria (Comercio Exterior).</summary>
    public string CatalogoFraccionArancelaria { get; set; } = "SatFraccionArancelaria";

    /// <summary>Catálogo remoto de c_UnidadAduana (Comercio Exterior).</summary>
    public string CatalogoUnidadAduana { get; set; } = "SatUnidadAduana";

    /// <summary>Catálogo remoto de c_Pais (Comercio Exterior).</summary>
    public string CatalogoPais { get; set; } = "SatCountries";

    /// <summary>Catálogo remoto de c_ClavePedimento (Comercio Exterior).</summary>
    public string CatalogoClavePedimento { get; set; } = "SatClavePedimento";
}

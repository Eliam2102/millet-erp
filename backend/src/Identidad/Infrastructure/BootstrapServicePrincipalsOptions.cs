namespace Millet.Identidad.Infrastructure;

/// <summary>
/// Configuración del <c>BootstrapServicePrincipalsHostedService</c>. Se
/// bindea desde <c>Auth:ServicePrincipalsJson</c> (string JSON
/// serializado), parseado en arranque para alinear el path de config con
/// QA/Prod (D-QA-PROD-CONFIG del prompt PR A): el array completo se
/// guarda en KV como un solo secret <c>auth-service-principals-json</c>
/// y la app setting es una KV reference.
///
/// <para>
/// En dev local <c>appsettings.Development.json</c> usa el mismo formato
/// (string JSON serializado dentro del JSON de settings) para evitar
/// dual-path. La fricción de editar JSON-en-string en dev es aceptable
/// para fase 1 con 1-2 SPs. PowerShell trick rápido para serializar:
/// <c>($obj | ConvertTo-Json -Compress) -replace '"','\"'</c>.
/// </para>
/// </summary>
public sealed class BootstrapServicePrincipalsOptions
{
    public const string SectionName = "Auth:ServicePrincipalsJson";

    /// <summary>
    /// JSON serializado de un array de
    /// <see cref="ServicePrincipalConfig"/>. Vacío o null = bootstrap
    /// no registra ningún SP (útil en tests y en arranques iniciales
    /// antes de que el operador suba el secret a KV).
    /// </summary>
    public string Json { get; set; } = string.Empty;
}

/// <summary>
/// Forma deserializada de cada entrada del array de SPs. <see cref="EmpresaRfc"/>
/// se resuelve a EmpresaId vía <c>IEmpresaResolverPort</c> en el bootstrap.
/// </summary>
public sealed class ServicePrincipalConfig
{
    public string Name { get; set; } = string.Empty;
    public Guid AppId { get; set; }
    public Guid ObjectId { get; set; }
    public string EmpresaRfc { get; set; } = string.Empty;
    public List<string> Permisos { get; set; } = new();
    public string? Notes { get; set; }
}

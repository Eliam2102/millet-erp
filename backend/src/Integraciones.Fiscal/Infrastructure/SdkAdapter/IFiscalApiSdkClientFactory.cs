using SdkClient = Fiscalapi.Abstractions.IFiscalApiClient;

namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// Factory + cache de instancias del SDK <c>Fiscalapi.IFiscalApiClient</c>
/// por empresa Millet.
///
/// <para>
/// El SDK <c>FiscalApiClient</c> se instancia con <c>FiscalapiSettings</c>
/// (ApiUrl + ApiKey + Tenant + ApiVersion + TimeZone). Cada empresa
/// Millet usa su propia <c>ApiKey</c> (resuelta del
/// <c>ConfiguracionPac</c> cifrado) y el <c>Tenant</c> global de las
/// <see cref="FiscalApiSdkAdapterOptions"/>.
/// </para>
///
/// <para>
/// <b>Cache</b>: <c>IFiscalApiClient</c> del SDK es thread-safe y mantiene
/// un <c>HttpClient</c> internamente — instanciarlo en cada llamada
/// crearía socket exhaustion. La implementación cachea por
/// <c>(empresaId, apiKeyHash)</c> con TTL del cache de
/// <see cref="Domain.Ports.IConfiguracionPacResolver"/> (60s); al rotar
/// la api key, el resolver invalida y el siguiente
/// <see cref="GetClientAsync"/> crea instancia nueva.
/// </para>
/// </summary>
public interface IFiscalApiSdkClientFactory
{
    /// <summary>
    /// Resuelve la configuración de la empresa, descifra la api key, y
    /// devuelve un <c>IFiscalApiClient</c> del SDK listo para usar.
    /// Lanza <see cref="System.InvalidOperationException"/> si no hay
    /// configuración activa o si <see cref="FiscalApiSdkAdapterOptions.Disabled"/>
    /// es true.
    /// </summary>
    Task<SdkClient> GetClientAsync(Guid empresaId, CancellationToken cancellationToken);
}

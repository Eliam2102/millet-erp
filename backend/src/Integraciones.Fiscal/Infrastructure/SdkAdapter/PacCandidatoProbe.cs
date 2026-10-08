using System.Diagnostics;
using Fiscalapi.Common;
using Fiscalapi.Services;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

public sealed class PacCandidatoProbe(IOptionsMonitor<FiscalApiSdkAdapterOptions> options, IClock clock)
    : IPacCandidatoProbe
{
    public async Task<PingResultDto> ProbarAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        ConfiguracionPac.ValidarBaseUrl(baseUrl);
        var opts = options.CurrentValue;
        if (opts.Disabled || string.IsNullOrWhiteSpace(opts.TenantKey))
            return new(false, 501, "Integración fiscal no habilitada.", 0, clock.UtcNow);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var watch = Stopwatch.StartNew();
        try
        {
            // Instancia transitoria: nunca sustituye el cliente ni la configuración vigente.
            var client = FiscalApiClient.Create(new FiscalapiSettings
            {
                ApiUrl = baseUrl, ApiKey = apiKey, ApiVersion = opts.ApiVersion,
                Tenant = opts.TenantKey, TimeZone = opts.TimeZone,
            });
            var response = await client.DownloadCatalogs.GetListAsync().WaitAsync(timeout.Token);
            return new(response.Succeeded, response.HttpStatusCode, "Prueba de credenciales.",
                watch.ElapsedMilliseconds, clock.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, 0, "Timeout del PAC.", watch.ElapsedMilliseconds, clock.UtcNow);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            // No registrar excepciones del SDK: pueden incluir credenciales o payloads.
            return new(false, 0, "No fue posible conectar con FiscalAPI.", watch.ElapsedMilliseconds, clock.UtcNow);
        }
    }
}

using System.Collections.Concurrent;
using Fiscalapi.Common;
using Fiscalapi.Services;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Exceptions;
using Millet.Integraciones.Fiscal.Domain.Ports;
using SdkClient = Fiscalapi.Abstractions.IFiscalApiClient;

namespace Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

/// <summary>
/// Implementación con cache de <see cref="IFiscalApiSdkClientFactory"/>.
/// Reusa instancias del SDK por (empresa, hash de api key); invalida
/// cuando el resolver detecta rotación.
/// </summary>
public sealed class FiscalApiSdkClientFactory : IFiscalApiSdkClientFactory
{
    private readonly IConfiguracionPacResolver _resolver;
    private readonly IOptionsMonitor<FiscalApiSdkAdapterOptions> _options;
    private readonly ConcurrentDictionary<string, SdkClient> _cache = new();

    public FiscalApiSdkClientFactory(
        IConfiguracionPacResolver resolver,
        IOptionsMonitor<FiscalApiSdkAdapterOptions> options)
    {
        _resolver = resolver;
        _options = options;
    }

    public async Task<SdkClient> GetClientAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var opts = _options.CurrentValue;
        if (opts.Disabled)
            throw new IntegracionFiscalNoHabilitadaException(
                "FiscalApiSdkAdapter está deshabilitado (IntegracionesFiscal:Sdk:Disabled=true).");
        if (string.IsNullOrWhiteSpace(opts.TenantKey))
            throw new IntegracionFiscalNoHabilitadaException(
                "FiscalApiSdkAdapter requiere TenantKey configurado (IntegracionesFiscal:Sdk:TenantKey).");

        var config = await _resolver.ResolverAsync(empresaId, ProveedorPac.FiscalApi, cancellationToken)
            ?? throw new ConfiguracionPacNoDisponibleException(empresaId);
        ConfiguracionPac.ValidarBaseUrl(config.BaseUrl);

        // Cache key incluye empresa + un hash de la api key (sin
        // material crudo) para que el cache se invalide al rotar.
        var cacheKey = $"{empresaId}:{config.ApiKey.GetHashCode()}";

        return _cache.GetOrAdd(cacheKey, _ =>
        {
            var settings = new FiscalapiSettings
            {
                ApiUrl     = config.BaseUrl,
                ApiKey     = config.ApiKey,
                ApiVersion = opts.ApiVersion,
                Tenant     = opts.TenantKey,
                TimeZone   = opts.TimeZone,
            };
            return FiscalApiClient.Create(settings);
        });
    }
}

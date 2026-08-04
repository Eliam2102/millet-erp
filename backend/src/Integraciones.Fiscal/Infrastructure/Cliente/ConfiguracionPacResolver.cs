using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Infrastructure.Cliente;

/// <summary>
/// Implementación productiva de <see cref="IConfiguracionPacResolver"/>.
/// Cache <c>IMemoryCache</c> con TTL absoluto de 60s — la rotación de
/// ApiKey se ve en máximo 60s incluso sin invalidación explícita.
///
/// <para>
/// <b>Singleton</b>: el cache es proceso-wide. Para acceder al
/// <c>IntegracionesFiscalDbContext</c> (scoped) inyectamos
/// <see cref="IServiceScopeFactory"/> y creamos scope propio en cada
/// resolución cache-miss.
/// </para>
///
/// <para>
/// <b>Bypass de tenancy</b>: usa <c>ICurrentEmpresaContext.Bypass()</c>
/// para que el resolver funcione tanto desde HTTP requests (donde la
/// empresa actual coincide con la query) como desde workers de fondo
/// (donde NO hay sesión y se itera sobre todas las empresas activas).
/// </para>
/// </summary>
internal sealed class ConfiguracionPacResolver : IConfiguracionPacResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;

    public ConfiguracionPacResolver(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
    }

    public async Task<ConfiguracionPacResuelta?> ResolverAsync(
        Guid empresaId,
        ProveedorPac proveedor,
        CancellationToken cancellationToken)
    {
        var key = CacheKey(empresaId, proveedor);
        if (_cache.TryGetValue<ConfiguracionPacResuelta?>(key, out var cached))
        {
            return cached;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>();
        var cipher = scope.ServiceProvider.GetRequiredService<FiscalSecretCipher>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        using var bypass = empresaContext.Bypass();

        var config = await db.ConfiguracionesPac
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.EmpresaId == empresaId && c.Proveedor == proveedor && c.Activo,
                cancellationToken);

        ConfiguracionPacResuelta? resuelta = null;
        if (config is not null)
        {
            resuelta = new ConfiguracionPacResuelta(
                ConfiguracionId: config.Id,
                EmpresaId: config.EmpresaId,
                Proveedor: config.Proveedor,
                BaseUrl: config.BaseUrl,
                ApiKey: cipher.Decrypt(config.ApiKeyCifrado),
                Activo: config.Activo,
                EmisorSandbox: MapIdentidad(config.EmisorSandbox),
                ReceptorSandbox: MapIdentidad(config.ReceptorSandbox),
                Csd: config.CsdConfigurado
                    ? new CsdResuelto(
                        CertificadoBase64: cipher.Decrypt(config.CsdCertificadoCifrado!),
                        LlavePrivadaBase64: cipher.Decrypt(config.CsdLlavePrivadaCifrada!),
                        Password: cipher.Decrypt(config.CsdPasswordCifrado!))
                    : null);
        }

        _cache.Set(key, resuelta, CacheTtl);
        return resuelta;
    }

    public void Invalidar(Guid empresaId, ProveedorPac proveedor)
    {
        _cache.Remove(CacheKey(empresaId, proveedor));
    }

    private static string CacheKey(Guid empresaId, ProveedorPac proveedor) =>
        $"fiscal-pac:{empresaId:N}:{(short)proveedor}";

    private static IdentidadSandboxResuelta? MapIdentidad(IdentidadSandbox? i) =>
        i is null ? null : new IdentidadSandboxResuelta(i.Rfc, i.RazonSocial, i.RegimenFiscal, i.CodigoPostal);
}

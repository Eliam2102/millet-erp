using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Stubs;

namespace Millet.Tesoreria.Infrastructure;

/// <summary>
/// Wiring de DI del módulo Tesorería (TES-PR1/PR2).
///
/// <para>
/// El DbContext se registra en <c>Program.cs</c> de Api (junto con los
/// demás módulos) para compartir el wiring de interceptors transversales
/// y la connection string. <c>IProveedorBancoReadPort</c> llega en PR-3,
/// el <c>OutboxPublisherWorker&lt;TesoreriaDbContext&gt;</c> y el
/// publisher de eventos en PR-4, y los listeners de CxC/Facturación en
/// PR-7 — todos condicionados a Service Bus en Program.cs, mismo patrón
/// que la triada.
/// </para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddTesoreriaModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // TES-PR2 (RN-8): candado de período contable — stub siempre-abierto
        // hasta que exista Contabilidad. PLATFORM-TODO(<PeriodoContableCerrado>).
        services.AddSingleton<IPeriodoContablePort, NoOpPeriodoContablePort>();

        // TES-PR3 [T-G1]: datos bancarios del proveedor desde DatosMaestros
        // (resuelve PLATFORM-TODO(PayloadEnriquecido) de CxP).
        services.AddScoped<Domain.Ports.DatosMaestros.IProveedorBancoReadPort,
            Adapters.ProveedorBancoReadPortAdapter>();

        // TES-PR7: nombres de cliente para la bandeja de depósitos (ADR-0042).
        services.AddScoped<Domain.Ports.DatosMaestros.IClienteReadPort,
            Adapters.ClienteReadPortAdapter>();

        // TES-PR8 (ADR-0024): XML de REPP recibidos a blob — mismo toggle
        // que el ICfdiBlobStorage de CxP: con connection string usa Azure
        // Blob real; sin ella cae al stub filesystem local (efímero).
        services
            .AddOptions<Blob.ReppBlobStorageOptions>()
            .Bind(configuration.GetSection(Blob.ReppBlobStorageOptions.SectionName));

        var reppBlobConnString = configuration
            .GetSection(Blob.ReppBlobStorageOptions.SectionName)
            .GetValue<string>(nameof(Blob.ReppBlobStorageOptions.ConnectionString));
        if (!string.IsNullOrWhiteSpace(reppBlobConnString))
        {
            // TryAdd: los demás módulos blob (Compras/Almacén/Aw/CxP) usan
            // la misma cuenta de Storage — se reusa el client si ya existe.
            services.TryAddSingleton(_ => new Azure.Storage.Blobs.BlobServiceClient(reppBlobConnString));
            services.AddSingleton<Domain.Ports.IReppXmlBlobStorage, Blob.AzureBlobReppXmlStorage>();
        }
        else
        {
            services.AddSingleton<Domain.Ports.IReppXmlBlobStorage, Blob.LocalFilesystemReppXmlStorage>();
        }

        return services;
    }
}

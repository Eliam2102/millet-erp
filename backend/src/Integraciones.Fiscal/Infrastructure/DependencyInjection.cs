using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cliente;
using Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;
using Millet.Integraciones.Fiscal.Infrastructure.Workers;

namespace Millet.Integraciones.Fiscal.Infrastructure;

/// <summary>
/// Wiring DI del módulo Integraciones.Fiscal. El registro del
/// <c>IntegracionesFiscalDbContext</c> + interceptors transversales se
/// hace en <c>Program.cs</c> (mismo patrón que Integraciones.Aw).
///
/// <para>
/// <b>Stack actual</b> (post PR-13):
/// </para>
/// <list type="bullet">
///   <item><b>MediatR + FluentValidation</b>: commands/queries del módulo.</item>
///   <item><b>IMemoryCache + IConfiguracionPacResolver</b>: resuelve
///   credenciales por empresa con cache TTL 60s.</item>
///   <item><b>SDK NuGet oficial Fiscalapi</b> envuelto en
///   <see cref="IFiscalApiSdkClient"/> — wrapper interno que maneja
///   resiliencia (timeouts, retry) internamente.</item>
///   <item><b>Workers Submitter + Poller</b> del flujo asíncrono (PR-11).</item>
///   <item><b>NoOpFiscalCfdiReceiver</b> default — el adapter real
///   en CxP lo sobreescribe en Program.cs (PR-12/PR-14).</item>
/// </list>
///
/// <para>
/// <c>FiscalSecretCipher</c> se registra desde <c>Program.cs</c>
/// (PR-3) porque depende del wiring transversal de DataProtection.
/// </para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddIntegracionesFiscalModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Memory cache + resolver con TTL 60s. AddMemoryCache es
        // idempotente (lo agregan otros módulos también).
        services.AddMemoryCache();
        services.AddSingleton<IConfiguracionPacResolver, ConfiguracionPacResolver>();

        // Worker options (Submitter + Poller).
        services.AddOptions<IntegracionesFiscalWorkerOptions>()
            .Bind(configuration.GetSection(IntegracionesFiscalWorkerOptions.SectionName));

        // SDK adapter NuGet oficial Fiscalapi. Factory cachea instancias
        // del SDK por (empresa, hash de api key). El adapter implementa
        // IFiscalApiSdkClient — el puerto que los workers usan.
        services.AddOptions<FiscalApiSdkAdapterOptions>()
            .Bind(configuration.GetSection(FiscalApiSdkAdapterOptions.SectionName));
        services.AddSingleton<IFiscalApiSdkClientFactory, FiscalApiSdkClientFactory>();
        services.AddScoped<IFiscalApiSdkClient, FiscalApiSdkAdapter>();

        // Búsqueda de catálogos SAT en vivo (typeahead de claves
        // producto/servicio, unidad y objeto de impuesto — FAC-DET-PR1).
        services.AddScoped<ISatCatalogosSearchPort, SatCatalogosSearchAdapter>();

        // Workers del flujo asíncrono. Default Disabled=true en
        // appsettings hasta que se valide en QA con FIEL real.
        services.AddHostedService<DescargaSubmitterWorker>();
        services.AddHostedService<DescargaPollerWorker>();

        // Receiver inverso: NoOp default. Program.cs lo sobreescribe con
        // FiscalCfdiReceiverAdapter (CxP) — el AddScoped posterior gana
        // porque TryAddScoped solo registra si no existe.
        services.TryAddScoped<IFiscalCfdiReceiver, NoOpFiscalCfdiReceiver>();

        // F2-PR1 (Facturación): repo de archivos CFDI emitidos
        // (cfdi_archivo). Facturación lo consume vía ICfdiRepositorioPort
        // reemplazando su stub local.
        services.AddScoped<
            Millet.Integraciones.Fiscal.Domain.Ports.ICfdiRepositorioPort,
            Cfdi.CfdiArchivoRepository>();

        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Evidencias;
using Millet.CuentasPorPagar.Domain.Notificaciones;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.Almacen;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Domain.Ports.Contabilidad;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Blob;
using Millet.CuentasPorPagar.Domain.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Mailbox;
using Millet.CuentasPorPagar.Infrastructure.Notificaciones;
using Millet.CuentasPorPagar.Infrastructure.Parsing;
using Millet.CuentasPorPagar.Infrastructure.Stubs;
using Millet.CuentasPorPagar.Infrastructure.Workers;

namespace Millet.CuentasPorPagar.Infrastructure;

/// <summary>
/// Wiring de DI del módulo Cuentas por Pagar (F0-PR1). Registra los 9
/// puertos cross-module con sus stubs <c>NoOp*</c> por defecto. Cada
/// stub lleva su propio <c>PLATFORM-TODO</c> en código indicando cuándo
/// y cómo reemplazarlo por el adapter real (ADR-0031).
///
/// <para>
/// El DbContext se registra en <c>Program.cs</c> de Api (junto con los
/// demás módulos) para compartir el wiring de interceptors transversales
/// y la connection string. Aquí solo viven los registros que son
/// puramente "internos" del módulo y no requieren visibilidad del API.
/// </para>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCuentasPorPagarModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // F1-PR1: parser + blob storage. Mismo toggle que Compras/Almacén/Aw:
        // con `CuentasPorPagar:Cfdi:BlobStorage:ConnectionString` configurada
        // (KV en QA/Prod) se usa Azure Blob real; sin ella, el stub filesystem
        // local (dev). Cierra PLATFORM-TODO(<CfdiBlobStorage>).
        services.AddSingleton<IXmlCfdiParser, XmlCfdiParser>();
        services
            .AddOptions<LocalFilesystemCfdiBlobStorageOptions>()
            .Bind(configuration.GetSection(LocalFilesystemCfdiBlobStorageOptions.SectionName));
        services
            .AddOptions<AzureCfdiBlobOptions>()
            .Bind(configuration.GetSection(AzureCfdiBlobOptions.SectionName));

        var cfdiBlobConnString = configuration
            .GetSection(AzureCfdiBlobOptions.SectionName)
            .GetValue<string>(nameof(AzureCfdiBlobOptions.ConnectionString));
        if (!string.IsNullOrWhiteSpace(cfdiBlobConnString))
        {
            // TryAdd: si Compras/Almacén/Aw ya registraron el BlobServiceClient
            // (misma cuenta de Storage en prod single-tenant), se reusa.
            services.TryAddSingleton(
                _ => new Azure.Storage.Blobs.BlobServiceClient(cfdiBlobConnString));
            services.AddSingleton<ICfdiBlobStorage, AzureBlobCfdiBlobStorage>();
        }
        else
        {
            services.AddSingleton<ICfdiBlobStorage, LocalFilesystemCfdiBlobStorage>();
        }

        // Cleanup final: workers SAT (descarga + refresh) viven ahora en
        // Millet.Integraciones.Fiscal. Aquí queda solo el scheduling de
        // mailbox + RFCs.
        services
            .AddOptions<WorkerSchedulingOptions>()
            .Bind(configuration.GetSection(WorkerSchedulingOptions.SectionName));

        // F2-PR2: mailbox dedicado de CFDIs vía Microsoft Graph.
        services
            .AddOptions<MailboxOptions>()
            .Bind(configuration.GetSection(MailboxOptions.SectionName));

        var mailboxOpts = configuration.GetSection(MailboxOptions.SectionName).Get<MailboxOptions>()
                          ?? new MailboxOptions();

        if (mailboxOpts.IsConfigured)
        {
            services.AddSingleton<IMailboxClient, GraphMailboxClient>();
        }
        else
        {
            services.AddSingleton<IMailboxClient, NoOpMailboxClient>();
        }

        services.AddHostedService<CfdiMailboxIngestionWorker>();

        // F4-PR2: evidencias polimórficas + worker SLA + INotificacionService stub.
        services.AddSingleton<IEvidenciaBlobStorage, LocalFilesystemEvidenciaBlobStorage>();
        services.AddSingleton<INotificacionService, NoOpNotificacionService>();
        services
            .AddOptions<RevisionSlaNotificacionOptions>()
            .Bind(configuration.GetSection(RevisionSlaNotificacionOptions.SectionName));
        services.AddHostedService<RevisionSlaNotificacionWorker>();

        // F6-PR1: worker de match de NCs EnEspera (A19, diario).
        services
            .AddOptions<NotaCreditoEnEsperaOptions>()
            .Bind(configuration.GetSection(NotaCreditoEnEsperaOptions.SectionName));
        services.AddHostedService<NotaCreditoEnEsperaMatchWorker>();

        // GI-PR1 (doc 12): emisor de reposiciones de caja chica —
        // compartido por Aplicar (respeta mínimo) y el corte manual.
        services.AddScoped<Application.ComprobacionGastos.Reposiciones.ReposicionCajaChicaEmisor>();

        // Ports cross-module — todos arrancan con NoOp en F0-PR1.
        // Cada port se reemplaza por su adapter real en el PR indicado.
        // PLATFORM-TODO cerrados aquí: <SucursalReadPort>, <ProveedorReadPort>,
        // <EmpleadoReadPort>, <PuestosEnAdmin> (ADM-PR2, doc
        // 10-catalogo-puestos-empleados).
        services.AddScoped<ISucursalReadPort, Adapters.SucursalReadPortAdapter>();
        services.AddScoped<IEmpleadoReadPort, Adapters.EmpleadoReadPortAdapter>();
        services.AddScoped<IPuestoReadPort, Adapters.PuestoReadPortAdapter>();
        services.AddScoped<IDependenciaRevisoraReadPort, NoOpDependenciaRevisoraReadPort>();
        services.AddScoped<ITipoCambioReadPort, NoOpTipoCambioReadPort>();
        services.AddScoped<IProveedorReadPort, Adapters.ProveedorReadPortAdapter>();
        services.AddScoped<IArticuloReadPort, NoOpArticuloReadPort>();
        // F5-PR1: ComprasOcReadPort y AlmacenRecepcionReadPort tienen
        // adapter real ahora; los NoOp* se conservan en código pero
        // dejan de wirearse en runtime.
        services.AddScoped<IComprasOcReadPort, Compras.ComprasOcReadPortAdapter>();
        services.AddScoped<IConceptoContableReadPort, NoOpConceptoContableReadPort>();
        services.AddScoped<IAlmacenRecepcionReadPort, Almacen.AlmacenRecepcionReadPortAdapter>();

        // F7-PR5: parser de estados de cuenta TC + algoritmo de conciliación.
        services.AddSingleton<
            Millet.CuentasPorPagar.Domain.Ports.TarjetaCredito.IEstadoCuentaTcParserPort,
            TarjetaCredito.EstadoCuentaTcExcelParser>();
        services.AddScoped<
            Millet.CuentasPorPagar.Domain.Ports.TarjetaCredito.IConciliacionAutomaticaService,
            TarjetaCredito.ConciliacionAutomaticaService>();

        return services;
    }
}

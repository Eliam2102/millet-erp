using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Millet.Integraciones.Aw.Application;
using Millet.Integraciones.Aw.Application.Ports;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Infrastructure.Adapters;
using Millet.Integraciones.Aw.Infrastructure.Repositories;

namespace Millet.Integraciones.Aw.Infrastructure;

/// <summary>
/// Wiring DI del módulo Integraciones.Aw. El registro del
/// <c>IntegracionesAwDbContext</c> + los interceptors transversales se
/// hace en <c>Program.cs</c> (sigue el patrón de Compras: cada módulo
/// expone sus servicios pero el DbContext se registra cerca del
/// connection string).
///
/// <para>
/// PR C registra adapters reales + workers + healthchecks + named
/// HttpClient. <c>OutboxPublisherWorker&lt;IntegracionesAwDbContext&gt;</c>
/// sigue registrándose en <c>Program.cs</c> porque depende del wiring
/// transversal del sender y named options keyed por DbContext.
/// </para>
///
/// <para>
/// <b>Pattern singleton + IHostedService:</b> los workers se registran
/// como singleton + via <c>AddHostedService(sp =&gt; sp.GetRequiredService&lt;T&gt;())</c>
/// para que el healthcheck reciba la MISMA instancia que el host está
/// ejecutando. Sin esto, <c>AddHostedService&lt;T&gt;()</c> + inyectar T en
/// el healthcheck dan instancias distintas y el flag <c>IsRunning</c>
/// nunca es visible al check.
/// </para>
/// </summary>
public static class DependencyInjection
{
    private static readonly string[] LivenessTags = ["liveness"];

    public static IServiceCollection AddIntegracionesAwModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<IntegracionesAwOptions>()
            .Bind(configuration.GetSection(IntegracionesAwOptions.SectionName));

        // ───────── PR Soketi — opciones del cliente Pusher ─────────
        services.AddOptions<SoketiOptions>()
            .Bind(configuration.GetSection(SoketiOptions.SectionName));

        services.AddScoped<IEntidadExternaRepository, EntidadExternaRepository>();

        // ───────── PR Soketi — Agent realtime publisher ─────────
        //
        // Switch dinámico según config: si Soketi tiene secret + appId,
        // usa Pusher contra el server de Agent; sino NoOp (dev local).
        // Equivalente al patrón ServiceBusClient registrado / no según
        // connection string.
        var soketiSection = configuration.GetSection(SoketiOptions.SectionName);
        var soketiSnapshot = new SoketiOptions();
        soketiSection.Bind(soketiSnapshot);
        if (soketiSnapshot.IsEnabled)
        {
            services.AddSingleton<IAgentRealtimePublisher, SoketiAgentRealtimePublisher>();
        }
        else
        {
            services.AddSingleton<IAgentRealtimePublisher, NoOpAgentRealtimePublisher>();
        }

        // ───────── feature aw-documentos-pdf — webhook live ERP→Agent ─────────
        // Mismo patrón toggle NoOp: si hay Url + Secret, POST server-to-server al
        // Agent al adjuntar el PDF; sino NoOp (el cron del Agent es el respaldo).
        services.AddOptions<AgentWebhookOptions>()
            .Bind(configuration.GetSection(AgentWebhookOptions.SectionName));
        var agentWebhookSnapshot = new AgentWebhookOptions();
        configuration.GetSection(AgentWebhookOptions.SectionName).Bind(agentWebhookSnapshot);
        if (agentWebhookSnapshot.IsEnabled)
        {
            services.AddScoped<IAgentDocumentoWebhook, HttpAgentDocumentoWebhook>();
        }
        else
        {
            services.AddScoped<IAgentDocumentoWebhook, NoOpAgentDocumentoWebhook>();
        }
        services.AddHttpClient(HttpAgentDocumentoWebhook.HttpClientName, (sp, client) =>
        {
            var opts = sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AgentWebhookOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds > 0 ? opts.TimeoutSeconds : 5);
        });

        // ───────── PR C — adapters reales ─────────
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IAwSqlReader, HybridConnectionAwSqlReader>();
        services.AddScoped<IAwDropAdapter, HttpAwDropAdapter>();

        // ───────── Late-reconciliation reader (PR-3 del feature) ─────────
        // Reusa el mismo named HttpClient — es el mismo servicio on-prem.
        services.AddScoped<IAwCompletionsReader, HttpAwCompletionsAdapter>();

        // ───────── Documents reader (feature aw-documentos-pdf) ─────────
        // Mismo named HttpClient — endpoints /documents del mismo servicio.
        services.AddScoped<IAwDocumentsReader, HttpAwDocumentsAdapter>();

        // Named HttpClient consumido por HttpAwDropAdapter y HttpAwCompletionsAdapter.
        services.AddHttpClient(HttpAwDropAdapter.HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<IntegracionesAwOptions>>().Value;
            client.BaseAddress = new Uri(options.DropServiceBaseAddress);
            client.Timeout = TimeSpan.FromSeconds(options.DropTimeoutSeconds);
        });

        // ───────── PR C — workers (singleton + IHostedService same instance) ─────────
        //
        // AwDropWorker requiere ServiceBusClient en constructor. Si Program.cs
        // no registró el client (porque no hay connection string SB), el
        // worker no se puede construir y la validación de DI falla. En ese
        // caso (dev local sin SB) los workers NO se registran y los
        // healthchecks reportan Unhealthy — comportamiento correcto: "no
        // estás configurado para procesar mensajes".
        var hasServiceBus = services.Any(d =>
            d.ServiceType == typeof(Azure.Messaging.ServiceBus.ServiceBusClient));

        if (hasServiceBus)
        {
            services.AddSingleton<AwDropWorker>();
            services.AddHostedService(sp => sp.GetRequiredService<AwDropWorker>());

            // PR #201 retiró AwCorrelationWorker — el flow per-EDI obtiene
            // el outcome (success/failed/stuck) en la misma respuesta HTTP
            // del drop service, sin polling SQL desde Azure.

            // ───────── Healthchecks liveness ─────────
            services.AddHealthChecks()
                .AddCheck<AwDropWorkerHealthCheck>(
                    "aw-drop-worker", tags: LivenessTags);
        }

        // ───────── PR-4 feature late-reconciliation ─────────
        // Timer-based, no requiere ServiceBus — se registra siempre que el
        // módulo esté wired. Patrón singleton + IHostedService para que el
        // health check reciba la misma instancia que el host ejecuta.
        services.AddSingleton<AwLateReconciliationWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<AwLateReconciliationWorker>());
        services.AddHealthChecks()
            .AddCheck<AwLateReconciliationWorkerHealthCheck>(
                "aw-late-reconciliation-worker", tags: LivenessTags);

        // ───────── feature aw-documentos-pdf ─────────
        // Timer-based, no requiere ServiceBus. Sube los PDF de A+W al ERP.
        // DocumentSyncKick: señal singleton que el flujo de correlación usa
        // para despertar al worker de PDF ni bien hay pedido (fetch dirigido
        // por evento en vez de esperar el intervalo idle).
        services.AddSingleton<DocumentSyncKick>();
        services.AddSingleton<AwDocumentSyncWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<AwDocumentSyncWorker>());
        services.AddHealthChecks()
            .AddCheck<AwDocumentSyncWorkerHealthCheck>(
                "aw-document-sync-worker", tags: LivenessTags);

        // NOTA: IIntegracionesAwNotifier se registra en Program.cs (su
        // implementación productiva IntegracionesAwHubNotifier vive en
        // Api/Hubs/ y depende de IHubContext<IntegracionesAwHub>).

        return services;
    }
}

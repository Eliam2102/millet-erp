using Azure.Messaging.ServiceBus;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Api.Infrastructure;

/// <summary>
/// C1.8 — entrega local de eventos de integración sin Service Bus (demo y
/// desarrollo). Con <c>ServiceBus:EntregaLocal:Habilitada=true</c> y sin
/// conexión a Service Bus, los <c>OutboxPublisherWorker</c> entregan cada
/// fila del outbox, en el mismo proceso, a los listeners reales de los
/// módulos suscritos al topic (<see cref="LocalDispatchIntegrationEventBusSender"/>)
/// en lugar de descartarla con el <see cref="NoOpIntegrationEventBusSender"/>.
///
/// <para>
/// <b>Guardas:</b> apagada por omisión; solo se permite en Development
/// (fuera de Development lanza al arrancar); con conexión a Service Bus
/// (Azure o <c>ServiceBus:Modo=EmuladorLocal</c>) la opción se ignora y
/// se registra un aviso.
/// </para>
///
/// <para>
/// <b>Agregar un consumidor nuevo</b> (p. ej. el listener de Contabilidad
/// del motor de pólizas, PLATFORM-TODO(&lt;MotorContable&gt;)): implementar
/// <see cref="ILocalIntegrationEventConsumer"/> en el worker y registrarlo
/// aquí además de como <c>AddHostedService</c> en el bloque con conexión
/// de <c>Program.cs</c>. Si el worker escucha varios topics (p. ej.
/// <c>ComprasActivoWorker</c>), registrar un consumidor local por topic.
/// </para>
/// </summary>
public static class EntregaLocalIntegrationEventsExtensions
{
    public const string ClaveHabilitada = "ServiceBus:EntregaLocal:Habilitada";

    /// <summary>
    /// Registra la entrega local si la opción está activa. Se llama solo en
    /// la rama sin conexión a Service Bus de <c>Program.cs</c>. Devuelve
    /// <c>true</c> si registró el <see cref="IIntegrationEventBusSender"/>
    /// local; <c>false</c> si la opción está apagada (el caller registra el
    /// NoOp).
    /// </summary>
    public static bool AddEntregaLocalIntegrationEvents(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!configuration.GetValue<bool>(ClaveHabilitada))
        {
            return false;
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "La entrega local de eventos (C1.8) solo se permite en Development; " +
                "en producción configure Service Bus. Quite ServiceBus:EntregaLocal:Habilitada.");
        }

        // Los listeners se registran como consumidores locales (no como
        // HostedService): sin ServiceBusClient, solo se usa su ProcessAsync.
        // 14 workers con Service Bus → 15 consumidores locales (ComprasActivoWorker
        // aporta dos, uno por topic). AwDropWorker queda fuera a propósito: es el
        // adaptador hacia A+W, no consume eventos internos.
        Consumidor<Millet.Almacen.Infrastructure.Workers.CxpEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.CuentasPorPagar.Infrastructure.Workers.AlmacenEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.CuentasPorPagar.Infrastructure.Workers.TesoreriaEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.CuentasPorPagar.Infrastructure.Workers.ComprasEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.Compras.Infrastructure.Workers.CxpEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.Compras.Infrastructure.Workers.AlmacenEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.CuentasPorCobrar.Infrastructure.Workers.FacturacionEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.CuentasPorCobrar.Infrastructure.Workers.TesoreriaEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.Tesoreria.Infrastructure.Workers.CuentasPorPagarEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.Tesoreria.Infrastructure.Workers.CuentasPorCobrarEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.Tesoreria.Infrastructure.Workers.FacturacionEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.Facturacion.Infrastructure.Workers.TesoreriaEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        Consumidor<Millet.Almacen.Infrastructure.Workers.ComercioExteriorEventListenerWorker>(services, (c, f, l) => new(c, f, l));
        // ComprasActivoWorker escucha dos topics: un consumidor local por topic.
        services.AddSingleton<ILocalIntegrationEventConsumer>(sp => new Millet.ActivosFijos.Infrastructure.Workers.ComprasActivoAlmacenLocalConsumer(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILogger<Millet.ActivosFijos.Infrastructure.Workers.ComprasActivoWorker>>()));
        services.AddSingleton<ILocalIntegrationEventConsumer>(sp => new Millet.ActivosFijos.Infrastructure.Workers.ComprasActivoCxpLocalConsumer(
            sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILogger<Millet.ActivosFijos.Infrastructure.Workers.ComprasActivoWorker>>()));

        services.AddSingleton<IIntegrationEventBusSender, LocalDispatchIntegrationEventBusSender>();
        services.AddHostedService(sp => new AvisoEntregaLocal(
            sp.GetRequiredService<ILogger<AvisoEntregaLocal>>(),
            LogLevel.Warning,
            "Entrega local de eventos (C1.8) ACTIVA: los eventos del outbox se entregan en el mismo proceso, " +
            $"sin Service Bus, a {sp.GetServices<ILocalIntegrationEventConsumer>().Count()} consumidores. Solo para demo/desarrollo."));
        return true;
    }

    /// <summary>
    /// Se llama en la rama con conexión a Service Bus: si la opción quedó
    /// encendida, se ignora y se avisa en el log al arrancar.
    /// </summary>
    public static void AdvertirSiEntregaLocalIgnorada(this IServiceCollection services, IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>(ClaveHabilitada))
        {
            return;
        }

        services.AddHostedService(sp => new AvisoEntregaLocal(
            sp.GetRequiredService<ILogger<AvisoEntregaLocal>>(),
            LogLevel.Warning,
            "ServiceBus:EntregaLocal:Habilitada=true se ignora: hay conexión a Service Bus configurada (C1.8)."));
    }

    private static void Consumidor<T>(
        IServiceCollection services,
        Func<ServiceBusClient, IServiceScopeFactory, ILogger<T>, T> crear)
        where T : class, ILocalIntegrationEventConsumer
        => services.AddSingleton<ILocalIntegrationEventConsumer>(sp => crear(
            null!, sp.GetRequiredService<IServiceScopeFactory>(), sp.GetRequiredService<ILogger<T>>()));

    /// <summary>Escribe un aviso al arrancar el host (el logging aún no existe al registrar servicios).</summary>
    internal sealed class AvisoEntregaLocal(ILogger<AvisoEntregaLocal> logger, LogLevel nivel, string mensaje) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
#pragma warning disable CA2254 // Mensaje fijo construido en el registro.
            logger.Log(nivel, mensaje);
#pragma warning restore CA2254
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Opciones del <c>OutboxPublisherWorker</c> (F6-PR2). Bindeadas desde
/// la sección <c>Outbox</c> del <c>IConfiguration</c>. La connection
/// string viene de Key Vault en QA/Prod (referenciada en bicep).
/// </summary>
public sealed class OutboxPublisherOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Connection string del Service Bus. Si está vacía o null, el
    /// worker usa <c>NoOpIntegrationEventBusSender</c> (dev local sin
    /// emulador). Default vacío.
    /// </summary>
    public string ServiceBusConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del tópico al que se publica. Convención: un solo tópico
    /// por módulo, los consumers filtran por <c>EventType</c> via
    /// reglas de suscripción. Default <c>compras-events</c>.
    /// </summary>
    public string ServiceBusTopicName { get; set; } = "compras-events";

    /// <summary>Segundos entre polls. Default 5.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Máximo de filas a procesar por tick. Default 50.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Tras este número de intentos fallidos, la fila queda excluida
    /// del polling (dead-letter pasivo: queda en outbox para
    /// inspección manual + alerting). Default 10.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// Si <c>true</c>, el <c>BackgroundService</c> del worker NO entra
    /// al loop de polling (regresa inmediatamente de <c>ExecuteAsync</c>).
    /// Default <c>false</c>. Pensado para test runs: suites integration
    /// que comparten BD verían races si el worker tickea en paralelo
    /// con tests que manipulan filas. Los tests instancian el worker
    /// manualmente para controlar el timing del tick.
    /// </summary>
    public bool Disabled { get; set; }
}

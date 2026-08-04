using System.Diagnostics.Metrics;

namespace Millet.Api.Hubs;

/// <summary>
/// Métricas custom del CollaborationHub (Sprint 3, ADR-0006). Las exporta
/// el Azure Monitor OpenTelemetry Distro a Application Insights cuando el
/// host arranca con <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> set; en
/// dev local sin la connection string viven en proceso y son visibles vía
/// dotnet-counters.
///
/// <para>
/// Alcance: solo métricas <em>de plataforma del hub</em> (presencia, locks,
/// conexiones). Las métricas built-in de SignalR (mensajes enviados,
/// negotiates, errores HTTP) las cubre el AspNetCore meter automáticamente.
/// </para>
///
/// <para>
/// Tags estandarizados:
/// <list type="bullet">
///   <item><c>compras.hub.empresa.id</c> — tenant del evento.</item>
///   <item><c>compras.hub.entidad</c> — tipo de entidad del soft lock.</item>
///   <item><c>compras.hub.modo</c> — Viewing | Editing.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ComprasHubMeter : IDisposable
{
    /// <summary>Nombre del Meter — registrar en <c>WithMetrics(m =&gt; m.AddMeter(...))</c>.</summary>
    public const string Name = "Millet.Compras.Hub";

    private readonly Meter _meter;

    public ComprasHubMeter()
    {
        _meter = new Meter(Name);

        SoftLockTracked = _meter.CreateCounter<long>(
            "softlock.tracked",
            unit: "{lock}",
            description: "Total de soft locks declarados (ViewingResource o EditingResource).");

        SoftLockReleased = _meter.CreateCounter<long>(
            "softlock.released",
            unit: "{lock}",
            description: "Total de soft locks liberados explícitamente (LeaveResource o disconnect).");

        SoftLockExpired = _meter.CreateCounter<long>(
            "softlock.expired",
            unit: "{lock}",
            description: "Total de soft locks expirados por timeout sin heartbeat.");

        HubConnectionsDelta = _meter.CreateUpDownCounter<long>(
            "hub.connections.active",
            unit: "{connection}",
            description: "Conexiones activas al ComprasHub (delta — incremento al conectar, decremento al desconectar).");
    }

    public Counter<long> SoftLockTracked { get; }
    public Counter<long> SoftLockReleased { get; }
    public Counter<long> SoftLockExpired { get; }
    public UpDownCounter<long> HubConnectionsDelta { get; }

    public void Dispose() => _meter.Dispose();
}

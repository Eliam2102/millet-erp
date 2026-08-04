using System.Diagnostics.Metrics;

namespace Millet.Identidad.Infrastructure.Telemetry;

/// <summary>
/// <see cref="Meter"/> singleton del módulo Identidad — emite counters
/// custom que el Azure Monitor OpenTelemetry Distro publica a Application
/// Insights cuando está activo (en dev local sin App Insights connection
/// string el counter sigue funcionando como no-op, sin falla).
///
/// <para>
/// Convención dot-separated del repo (igual que <c>ComprasHubMeter</c>).
/// El nombre del Meter (<see cref="Name"/>) se registra explícitamente
/// con <c>WithMetrics(metrics =&gt; metrics.AddMeter(...))</c> en
/// <c>Program.cs</c> para que el OTel Distro lo pickup.
/// </para>
/// </summary>
public sealed class IdentidadMeter : IDisposable
{
    public const string Name = "Millet.Identidad";

    private readonly Meter _meter;

    public Counter<long> SpBootstrapSkipped { get; }
    public Counter<long> SpBootstrapUpserted { get; }
    public Counter<long> SpResolutionUnknown { get; }
    public Counter<long> SpResolutionDisabled { get; }

    public IdentidadMeter()
    {
        _meter = new Meter(Name);

        SpBootstrapSkipped = _meter.CreateCounter<long>(
            "auth.sp.bootstrap.skipped",
            unit: "{records}",
            description: "Service principals que el bootstrap omitió por configuración inválida (empresa missing, appid duplicado, o excepción persistiendo).");

        SpBootstrapUpserted = _meter.CreateCounter<long>(
            "auth.sp.bootstrap.upserted",
            unit: "{records}",
            description: "Service principals que el bootstrap creó o actualizó correctamente.");

        SpResolutionUnknown = _meter.CreateCounter<long>(
            "auth.sp.resolution.unknown",
            unit: "{requests}",
            description: "Tokens de service principal con appid no registrado en BD (devolvió 403 unknown_service_principal).");

        SpResolutionDisabled = _meter.CreateCounter<long>(
            "auth.sp.resolution.disabled",
            unit: "{requests}",
            description: "Tokens de service principal con appid registrado pero el SP está Activo=false (devolvió 403 service_principal_disabled).");
    }

    public void Dispose() => _meter.Dispose();
}

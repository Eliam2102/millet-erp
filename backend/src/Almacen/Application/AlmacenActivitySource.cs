using System.Diagnostics;

namespace Millet.Almacen.Application;

/// <summary>
/// <see cref="ActivitySource"/> compartido del módulo Almacén (F0-PR1,
/// ADR-0006). Los handlers de movimientos (Recepción, Salida, Devolución,
/// Conteo) crean spans con <c>StartActivity()</c>; el Azure Monitor
/// OpenTelemetry Distro los exporta a Application Insights.
/// </summary>
public static class AlmacenActivitySource
{
    public const string Name = "Millet.Almacen";

    public static readonly ActivitySource Instance = new(Name);
}

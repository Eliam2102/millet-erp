using System.Diagnostics;

namespace Millet.CuentasPorPagar.Application;

/// <summary>
/// <see cref="System.Diagnostics.ActivitySource"/> compartido del módulo
/// Cuentas por Pagar (ADR-0006). Los handlers de captura, autorización,
/// aplicación de NC/anticipo y los workers de ingestión SAT/mailbox
/// crean spans con <c>StartActivity()</c>; el Azure Monitor OpenTelemetry
/// Distro los exporta a Application Insights.
///
/// <para>
/// Tag conventions:
/// <list type="bullet">
///   <item><c>cxp.factura.id</c> — Guid del agregado raíz.</item>
///   <item><c>cxp.cfdi.uuid</c> — UUID fiscal cuando aplica.</item>
///   <item><c>cxp.empresa.id</c> — tenant.</item>
///   <item><c>cxp.estado.antes</c> / <c>cxp.estado.despues</c> — transiciones.</item>
/// </list>
/// </para>
/// </summary>
public static class CuentasPorPagarActivitySource
{
    public const string Name = "Millet.CuentasPorPagar";

    public static readonly ActivitySource Instance = new(Name);
}

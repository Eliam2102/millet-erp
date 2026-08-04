using System.Diagnostics;

namespace Millet.Compras.Application;

/// <summary>
/// <see cref="System.Diagnostics.ActivitySource"/> compartido del módulo
/// Compras (F8-PR2, ADR-0006). Los handlers críticos (Autorizar, Cancelar,
/// RegistrarRecepcion) crean spans con <c>StartActivity()</c>; el Azure
/// Monitor OpenTelemetry Distro los exporta a Application Insights.
///
/// <para>
/// Tag conventions:
/// <list type="bullet">
///   <item><c>compras.requisicion.id</c> — Guid del agregado raíz.</item>
///   <item><c>compras.estado.antes</c> / <c>compras.estado.despues</c> — para transiciones.</item>
///   <item><c>compras.empresa.id</c> — tenant.</item>
/// </list>
/// </para>
/// </summary>
public static class ComprasActivitySource
{
    public const string Name = "Millet.Compras";

    public static readonly ActivitySource Instance = new(Name);
}

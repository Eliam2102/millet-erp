using System.Diagnostics;

namespace Millet.Integraciones.Aw.Application;

/// <summary>
/// <see cref="ActivitySource"/> del módulo Integraciones.Aw para
/// distributed tracing. Sigue el patrón de
/// <c>Millet.Compras.Application.ComprasActivitySource</c>.
///
/// <para>
/// Registrar en <c>Program.cs</c> via
/// <c>builder.Services.AddOpenTelemetry().WithTracing(t =&gt;
/// t.AddSource(IntegracionesAwActivitySource.Name))</c> para que las
/// activities producidas aquí lleguen a Application Insights / OTel
/// Distro.
/// </para>
/// </summary>
public static class IntegracionesAwActivitySource
{
    public const string Name = "Millet.Integraciones.Aw";
    public static readonly ActivitySource Instance = new(Name);
}

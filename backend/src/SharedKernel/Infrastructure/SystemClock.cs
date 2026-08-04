using System.Diagnostics.CodeAnalysis;
using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure;

/// <summary>
/// Implementación de producción de <see cref="IClock"/>. Es la única clase
/// del proyecto autorizada a llamar directamente a <c>DateTimeOffset.UtcNow</c>;
/// el analyzer <c>BannedApiAnalyzers</c> prohíbe el uso en cualquier otro
/// archivo. Ver ADR-0013.
/// </summary>
public sealed class SystemClock : IClock
{
    [SuppressMessage(
        "ApiDesign",
        "RS0030:Do not use banned APIs",
        Justification = "SystemClock es la única implementación permitida que llama a DateTimeOffset.UtcNow directamente. Todo el resto del código debe inyectar IClock. Ver ADR-0013.")]
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

namespace Millet.SharedKernel.Application;

/// <summary>
/// Abstracción del reloj. Inyectada vía DI en lugar de llamar directamente
/// a <c>DateTimeOffset.UtcNow</c>. Permite tests deterministas (TestClock)
/// y un único punto de control sobre la fuente de tiempo.
/// Ver ADR-0013.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

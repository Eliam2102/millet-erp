using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.Infrastructure;

/// <summary>
/// Reloj controlable para tests. Permite avanzar el tiempo determinísticamente
/// sin depender de la hora real del sistema. Reemplaza a <see cref="SystemClock"/>
/// en el contenedor de DI durante los tests. Ver ADR-0013 y ADR-0016.
/// </summary>
public sealed class TestClock : IClock
{
    private DateTimeOffset _now;

    public TestClock(DateTimeOffset initial) => _now = initial;

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    public void SetTo(DateTimeOffset now) => _now = now;
}

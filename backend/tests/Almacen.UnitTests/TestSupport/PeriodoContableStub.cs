using Millet.Almacen.Domain.Ports;

namespace Millet.Almacen.UnitTests.TestSupport;

/// <summary>Respuesta explícita del calendario para pruebas unitarias; no sustituye el adaptador en integración.</summary>
internal sealed class PeriodoContableStub(bool abierto) : IPeriodoContableReadPort
{
    public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) => Task.FromResult(abierto);
}

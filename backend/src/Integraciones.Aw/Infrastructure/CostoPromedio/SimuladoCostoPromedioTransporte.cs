using System.Collections.Concurrent;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

// PLATFORM-TODO(<CostoPromedioAlmacenes>): transporte simulado mientras TI no corre el script 19 en AlmacenRQ_MP_ENSAYO/AlmacenRQ_MP
// y no se habilita Integraciones:CostoPromedio:Transporte=Sql (ver docs/modulos/integraciones-aw/00-levantamiento.md §7).
/// <summary>Transporte en memoria (por omisión): guarda lo que se habría insertado. Mismo criterio de idempotencia que el SQL.</summary>
public sealed class SimuladoCostoPromedioTransporte : ICostoPromedioTransporte
{
    private readonly ConcurrentQueue<CostoPromedioEscrito> _filas = new();

    public IReadOnlyCollection<CostoPromedioEscrito> Filas => _filas.ToArray();

    public Task EscribirAsync(CostoPromedioEscrito escrito, CancellationToken ct)
    {
        if (!_filas.Any(f => f.Fila == escrito.Fila && f.Version == escrito.Version)) _filas.Enqueue(escrito);
        return Task.CompletedTask;
    }
}

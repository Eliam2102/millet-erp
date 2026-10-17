using Millet.Almacen.Domain.Ports;

namespace Millet.Almacen.Infrastructure.Stubs;

/// <summary>
/// PLATFORM-TODO(&lt;AdjuntosEntidadReadAdapter&gt;): reemplazado en Api/Program.cs por el adaptador de Compartido.
/// </summary>
public sealed class NoOpAdjuntosEntidadReadPort : IAdjuntosEntidadReadPort
{
    public Task<IReadOnlyList<AdjuntoEvidenciaLectura>> ListarVigentesAsync(
        string tipoEntidad, Guid entidadId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AdjuntoEvidenciaLectura>>([]);
}

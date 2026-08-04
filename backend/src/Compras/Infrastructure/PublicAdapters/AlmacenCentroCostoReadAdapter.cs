using Millet.Almacen.Domain.Ports;
using CcPorts = Millet.CentrosCosto.Application.PublicPorts;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="ICentroCostoReadPort"/> declarado en
/// <c>Almacen.Domain.Ports</c> (Fase E PR5). Reemplaza el
/// <c>NoOpCentroCostoReadPort</c> de Almacén (PLATFORM-TODO
/// &lt;AlmacenCentroCostoReadAdapter&gt;).
///
/// <para>Reside en <c>Compras.Infrastructure.PublicAdapters</c> —igual que
/// <see cref="ComprasOcReadAdapter"/> / <see cref="ComprasRequisicionReadAdapter"/>—
/// porque Compras YA referencia Almacén Y CentrosCosto; Almacén NO puede
/// referenciar CentrosCosto (cerraría un ciclo vía Compartido:
/// <c>CentrosCosto→Compartido→Almacen</c>). El composition root
/// (<c>Program.cs</c>) cablea esta implementación en lugar del NoOp.</para>
///
/// <para>Es un puente delgado: NO reimplementa la lectura, delega en
/// <c>CentrosCosto.Application.PublicPorts.IDim3ReadPort</c> (el read-port
/// público del owner) y mapea su proyección a la de Almacén. Sin filtro de
/// alcance, incluye inactivas (ADR-0050 §3 / ADR-0049).</para>
/// </summary>
public sealed class AlmacenCentroCostoReadAdapter : ICentroCostoReadPort
{
    private readonly CcPorts.IDim3ReadPort _dim3;

    public AlmacenCentroCostoReadAdapter(CcPorts.IDim3ReadPort dim3) => _dim3 = dim3;

    public async Task<IReadOnlyDictionary<Guid, Dim3Lectura>> ObtenerAsync(
        IReadOnlyCollection<Guid> dim3Ids, CancellationToken cancellationToken)
    {
        var origen = await _dim3.ObtenerAsync(dim3Ids, cancellationToken);
        var mapeado = new Dictionary<Guid, Dim3Lectura>(origen.Count);
        foreach (var (id, dim3) in origen)
        {
            mapeado[id] = new Dim3Lectura(dim3.Id, dim3.Clave, dim3.Nombre, dim3.Activa);
        }
        return mapeado;
    }
}

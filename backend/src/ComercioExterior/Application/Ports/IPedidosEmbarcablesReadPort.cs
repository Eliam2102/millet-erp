using Millet.ComercioExterior.Domain;

namespace Millet.ComercioExterior.Application.Ports;

/// <summary>Puerto inverso: Facturación conserva la única fuente del pedido.</summary>
public interface IPedidosEmbarcablesReadPort
{
    Task<IReadOnlyList<PedidoEmbarcable>> BuscarPorNumeroAsync(Guid empresaId, IReadOnlyCollection<string> numeros, CancellationToken ct);
    Task<IReadOnlyList<PedidoEmbarcable>> ListarAsync(Guid clienteId, Guid sucursalId, CancellationToken ct);
    Task<IReadOnlyList<PedidoEmbarcable>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

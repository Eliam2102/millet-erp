using Millet.ComercioExterior.Domain;
using Millet.ComercioExterior.Application;

namespace Millet.ComercioExterior.Application.Ports;

/// <summary>Puerto inverso: Facturación conserva la única fuente del pedido.</summary>
public interface IPedidosEmbarcablesReadPort
{
    Task<IReadOnlyList<PedidoEmbarcable>> BuscarPorNumeroAsync(Guid empresaId, IReadOnlyCollection<string> numeros, CancellationToken ct);
    Task<IReadOnlyList<PedidoEmbarcable>> ListarAsync(Guid clienteId, Guid sucursalId, CancellationToken ct);
    Task<bool> TieneFacturaContenedorAsync(Guid contenedorId, CancellationToken ct) => Task.FromResult(false);
    Task<bool> FacturasDespachoAsync(Guid contenedorId, IReadOnlyList<PedidoContenedorEvento> requeridas, DateOnly fecha, CancellationToken ct) => Task.FromResult(false);
    Task<IReadOnlyList<PedidoEmbarcable>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

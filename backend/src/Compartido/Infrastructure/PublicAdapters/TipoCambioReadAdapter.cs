using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto <see cref="ITipoCambioReadPort"/> de Almacén (C1.2):
/// lee el TC del catálogo <c>compartido.tipos_cambio</c>. Reemplaza el
/// <c>NoOpTipoCambioReadPort</c> (PLATFORM-TODO &lt;TipoCambioReadAdapter&gt;).
/// Sin TC para la fecha devuelve <c>null</c>, igual que el contrato del puerto.
/// </summary>
public sealed class TipoCambioReadAdapter(CompartidoDbContext db) : ITipoCambioReadPort
{
    public Task<decimal?> ObtenerTipoCambioMxnAsync(string monedaOrigen, DateOnly fecha, CancellationToken cancellationToken) =>
        TiposCambioCatalogo.ValorEnMxnAsync(db, monedaOrigen, fecha, cancellationToken);
}

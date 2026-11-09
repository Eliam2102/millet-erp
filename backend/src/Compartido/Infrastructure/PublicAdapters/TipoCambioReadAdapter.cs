using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de los puertos <see cref="ITipoCambioReadPort"/> (Almacén C1.2)
/// e <see cref="ITipoCambioRangoReadPort"/> (Compartido / Integraciones.Aw I13.2):
/// lee el TC del catálogo <c>compartido.tipos_cambio</c>. Reemplaza el
/// <c>NoOpTipoCambioReadPort</c> (PLATFORM-TODO &lt;TipoCambioReadAdapter&gt;).
/// Sin TC para la fecha devuelve <c>null</c>, igual que el contrato del puerto.
/// </summary>
public sealed class TipoCambioReadAdapter(CompartidoDbContext db) : ITipoCambioReadPort, ITipoCambioRangoReadPort
{
    public Task<decimal?> ObtenerTipoCambioMxnAsync(string monedaOrigen, DateOnly fecha, CancellationToken cancellationToken) =>
        TiposCambioCatalogo.ValorEnMxnAsync(db, monedaOrigen, fecha, cancellationToken);

    public Task<IReadOnlyList<TipoCambioRangoItem>> ObtenerRangoTipoCambioAsync(
        IReadOnlyList<string> monedas, DateOnly desde, DateOnly hasta, CancellationToken ct = default) =>
        TiposCambioCatalogo.RangoEnMxnAsync(db, monedas, desde, hasta, ct);
}

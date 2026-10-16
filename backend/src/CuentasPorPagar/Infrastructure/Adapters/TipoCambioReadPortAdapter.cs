using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

/// <summary>
/// Adapter productivo de <see cref="ITipoCambioReadPort"/> (C1.2): lee el catálogo
/// <c>compartido.tipos_cambio</c>, que guarda cada moneda en pesos. Entre dos monedas
/// extranjeras cruza por MXN. Sin TC de alguna de las dos para la fecha devuelve <c>null</c>.
/// </summary>
public sealed class TipoCambioReadPortAdapter(CompartidoDbContext db) : ITipoCambioReadPort
{
    public async Task<decimal?> ObtenerAsync(
        string monedaOrigen, string monedaDestino, DateOnly fecha, CancellationToken cancellationToken)
    {
        if (string.Equals(monedaOrigen.Trim(), monedaDestino.Trim(), StringComparison.OrdinalIgnoreCase)) return 1m;
        if (await TiposCambioCatalogo.ValorEnMxnAsync(db, monedaOrigen, fecha, cancellationToken) is not { } origen) return null;
        if (await TiposCambioCatalogo.ValorEnMxnAsync(db, monedaDestino, fecha, cancellationToken) is not { } destino) return null;
        return Math.Round(origen / destino, 6);
    }
}

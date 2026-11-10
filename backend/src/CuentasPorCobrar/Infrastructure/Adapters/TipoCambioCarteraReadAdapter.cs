using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.CuentasPorCobrar.Domain.Ports.Catalogos;

namespace Millet.CuentasPorCobrar.Infrastructure.Adapters;

/// <summary>
/// Adapter de <see cref="ITipoCambioCarteraReadPort"/>: misma consulta del catálogo que usan
/// Almacén, CxP, Tesorería y Contabilidad. Sin TC de esa fecha devuelve <c>null</c>; nunca inventa uno.
/// </summary>
public sealed class TipoCambioCarteraReadAdapter(CompartidoDbContext db) : ITipoCambioCarteraReadPort
{
    public Task<decimal?> ValorEnMxnAsync(string moneda, DateOnly fecha, CancellationToken cancellationToken) =>
        TiposCambioCatalogo.ValorEnMxnAsync(db, moneda, fecha, cancellationToken);
}

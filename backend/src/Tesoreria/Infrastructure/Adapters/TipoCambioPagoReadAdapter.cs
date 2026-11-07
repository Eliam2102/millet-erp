using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.Tesoreria.Domain.Ports;

namespace Millet.Tesoreria.Infrastructure.Adapters;

/// <summary>TES-09 (86e3jvvj4): TC del catálogo K10.6 para el pago o el cobro; misma consulta que Almacén y Contabilidad.</summary>
public sealed class TipoCambioPagoReadAdapter(CompartidoDbContext db) : ITipoCambioPagoReadPort
{
    public Task<decimal?> ValorEnMxnAsync(string moneda, DateOnly fecha, CancellationToken ct) =>
        TiposCambioCatalogo.ValorEnMxnAsync(db, moneda, fecha, ct);
}

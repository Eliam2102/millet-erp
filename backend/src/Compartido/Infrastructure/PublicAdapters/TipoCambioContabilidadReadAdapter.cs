using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Ports;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>CON-11 (86e3kdypd): TC del catálogo K10.6 para Contabilidad (diferencia cambiaria realizada y revaluación).</summary>
public sealed class TipoCambioContabilidadReadAdapter(CompartidoDbContext db) : ITipoCambioContabilidadReadPort
{
    public Task<decimal?> ValorEnMxnAsync(string moneda, DateOnly fecha, CancellationToken ct) =>
        TiposCambioCatalogo.ValorEnMxnAsync(db, moneda, fecha, ct);
}

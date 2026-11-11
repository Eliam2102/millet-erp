using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.ComercioExterior.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class TipoCambioGastoReadAdapter(CompartidoDbContext db) : ITipoCambioGastoReadPort
{
    public async Task<TipoCambioCatalogo?> ObtenerSugeridoAsync(DateOnly fecha, CancellationToken ct)
    {
        var fila = await (from t in db.TiposCambio.AsNoTracking() join m in db.Monedas on t.MonedaId equals m.Id
            where m.Codigo == "USD" && t.Fecha == fecha
            orderby t.Origen == OrigenTipoCambio.Banxico descending
            select new { t.ValorEnMxn, t.Origen }).FirstOrDefaultAsync(ct);
        return fila is null ? null : new TipoCambioCatalogo(fila.ValorEnMxn, fila.Origen.ToString());
    }
}

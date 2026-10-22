using Microsoft.EntityFrameworkCore;
using Millet.ComercioExterior.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.Infrastructure.PublicAdapters;
public sealed class TipoCambioGastoReadAdapter(CompartidoDbContext db) : ITipoCambioGastoReadPort
{
    public async Task<decimal> ObtenerBanxicoAsync(DateOnly fecha, CancellationToken ct)
    {
        var tc = await (from t in db.TiposCambio.AsNoTracking() join m in db.Monedas on t.MonedaId equals m.Id
            where m.Codigo == "USD" && t.Fecha == fecha && t.Origen == Millet.Catalogos.Domain.OrigenTipoCambio.Banxico
            select (decimal?)t.ValorEnMxn).SingleOrDefaultAsync(ct);
        return tc ?? throw new BusinessRuleException("CE_TC_BANXICO_FALTANTE", "Registre el tipo de cambio Banxico de la fecha de la factura en el catálogo de monedas.");
    }
}

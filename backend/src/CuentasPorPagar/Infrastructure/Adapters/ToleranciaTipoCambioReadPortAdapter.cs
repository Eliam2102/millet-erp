using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

public sealed class ToleranciaTipoCambioReadPortAdapter(CompartidoDbContext db) : IToleranciaTipoCambioReadPort
{
    public async Task<decimal> ToleranciaPctAsync(CancellationToken cancellationToken)
    {
        var valor = await db.ParametrosGlobales.AsNoTracking()
            .Where(p => p.Clave == ToleranciaTipoCambioCfdiParametro.Clave)
            .Select(p => p.Valor)
            .SingleOrDefaultAsync(cancellationToken);
        return valor is null ? ToleranciaTipoCambioCfdiParametro.ValorPorOmision : ToleranciaTipoCambioCfdiParametro.LeerValor(valor);
    }
}

using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;

namespace Millet.CuentasPorPagar.Infrastructure.Adapters;

public sealed class ToleranciaGeneralReadPortAdapter(CompartidoDbContext db) : IToleranciaGeneralReadPort
{
    public async Task<decimal> ObtenerMontoMxnAsync(CancellationToken cancellationToken)
    {
        var valor = await db.ParametrosGlobales.AsNoTracking()
            .Where(p => p.Clave == ToleranciaFacturaContraOcParametro.Clave)
            .Select(p => p.Valor)
            .SingleOrDefaultAsync(cancellationToken);
        return valor is null ? ToleranciaFacturaContraOcParametro.ValorPorOmision
            : ToleranciaFacturaContraOcParametro.LeerValor(valor);
    }
}

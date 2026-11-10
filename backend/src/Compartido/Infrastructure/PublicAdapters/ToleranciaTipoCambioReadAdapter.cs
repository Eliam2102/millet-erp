using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Ports;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>M-207 (86e3zr7nf): tolerancia del TC del CFDI contra el catálogo (parámetro global; sin fila, el valor por omisión).</summary>
public sealed class ToleranciaTipoCambioReadAdapter(CompartidoDbContext db) : IToleranciaTipoCambioReadPort
{
    public async Task<decimal> ToleranciaPctAsync(CancellationToken ct)
    {
        var valor = await db.ParametrosGlobales.AsNoTracking()
            .Where(p => p.Clave == ToleranciaTipoCambioCfdiParametro.Clave)
            .Select(p => p.Valor)
            .SingleOrDefaultAsync(ct);
        return valor is null ? ToleranciaTipoCambioCfdiParametro.ValorPorOmision : ToleranciaTipoCambioCfdiParametro.LeerValor(valor);
    }
}

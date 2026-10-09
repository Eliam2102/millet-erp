using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Parametros;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>Lee en una sola consulta la política global, sin caché ni valores alternativos.</summary>
public sealed class ConteoUmbralesProvider(CompartidoDbContext db) : IConteoUmbralesProvider
{
    public async Task<ConteoUmbrales> ObtenerAsync(CancellationToken cancellationToken)
    {
        var valores = await db.ParametrosGlobales.AsNoTracking()
            .Where(p => ParametrosUmbralesConteo.Claves.Contains(p.Clave))
            .ToDictionaryAsync(p => p.Clave, p => p.Valor, cancellationToken);
        return ParametrosUmbralesConteo.Leer(valores);
    }
}

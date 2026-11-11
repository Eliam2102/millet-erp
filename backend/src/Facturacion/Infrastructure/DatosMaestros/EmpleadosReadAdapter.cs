using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.DatosMaestros;

/// <summary>
/// Adapter de <see cref="IEmpleadosReadPort"/> sobre <c>compartido.empleados</c> vía <see cref="CompartidoDbContext"/>
/// (mismo precedente que <see cref="ClientesReadAdapter"/>; cero escritura). Misma normalización que el publicador del
/// catálogo de Pinturas (<c>PinturasCatalogosExtractor</c>: clave sin espacios).
/// </summary>
public sealed class EmpleadosReadAdapter(CompartidoDbContext db) : IEmpleadosReadPort
{
    public Task<bool> ExisteClaveAsync(string clave, CancellationToken cancellationToken)
    {
        var c = clave.Trim();
        return db.Empleados.AsNoTracking().AnyAsync(e => e.Clave.Trim() == c, cancellationToken);
    }
}

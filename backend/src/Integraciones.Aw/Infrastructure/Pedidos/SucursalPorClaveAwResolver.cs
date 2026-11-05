using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Adapter de <see cref="ISucursalPorClaveAwResolver"/> sobre el catálogo
/// compartido de sucursales; la clave A+W de una sucursal puede traer alias separados por coma. Solo machea sucursales activas: ingestar hacia
/// una sucursal desactivada debe caer a la bandeja, no pasar en silencio.
/// </summary>
public sealed class SucursalPorClaveAwResolver : ISucursalPorClaveAwResolver
{
    private readonly CompartidoDbContext _db;

    public SucursalPorClaveAwResolver(CompartidoDbContext db) => _db = db;

    public async Task<Guid?> ResolverAsync(
        string claveAw, CancellationToken cancellationToken)
    {
        var clave = claveAw.Trim().ToUpperInvariant();

        // Catálogo chico: se traen las activas con clave y se compara contra su lista
        // de claves/alias en memoria. Lo que no machea (REPOSICIONES, REEMPLAZOS,
        // MUESTRAS, RETRABAJO...: piezas de un pedido original, no sucursales) devuelve
        // null y el envío queda PorVincular hasta que administración defina la regla.
        var candidatas = await _db.Sucursales.AsNoTracking()
            .Where(s => s.ClaveAw != null && s.Estatus == EstatusCatalogo.Activo)
            .Select(s => new { s.Id, s.ClaveAw })
            .ToListAsync(cancellationToken);
        return candidatas.FirstOrDefault(s => Sucursal.ClavesAw(s.ClaveAw).Contains(clave))?.Id;
    }
}

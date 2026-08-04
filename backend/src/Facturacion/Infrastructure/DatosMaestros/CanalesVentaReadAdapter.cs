using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Ports;

namespace Millet.Facturacion.Infrastructure.DatosMaestros;

/// <summary>
/// Adapter REAL de <see cref="ICanalesVentaReadPort"/> sobre
/// <c>compartido.canales_venta</c> (dueño: Administración — FAC-ING-PR2).
/// Lectura vía <see cref="CompartidoDbContext"/>, mismo precedente que
/// <see cref="EmpresaFiscalReadAdapter"/>; cero escritura.
/// </summary>
public sealed class CanalesVentaReadAdapter : ICanalesVentaReadPort
{
    private readonly CompartidoDbContext _db;

    public CanalesVentaReadAdapter(CompartidoDbContext db) => _db = db;

    public async Task<bool> ExisteActivoAsync(
        short canalVentaId, CancellationToken cancellationToken)
    {
        return await _db.CanalesVenta.AsNoTracking()
            .AnyAsync(c => c.Id == canalVentaId && c.Estatus == EstatusCatalogo.Activo,
                cancellationToken);
    }

    public async Task<string?> ObtenerNombreAsync(
        short canalVentaId, CancellationToken cancellationToken)
    {
        // Sin filtro por estatus: los históricos con canal desactivado
        // siguen mostrando su nombre.
        return await _db.CanalesVenta.AsNoTracking()
            .Where(c => c.Id == canalVentaId)
            .Select(c => c.Nombre)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CanalVentaLectura>> ListarActivosAsync(
        CancellationToken cancellationToken)
    {
        return await _db.CanalesVenta.AsNoTracking()
            .Where(c => c.Estatus == EstatusCatalogo.Activo)
            .OrderBy(c => c.Id)
            .Select(c => new CanalVentaLectura(c.Id, c.Nombre))
            .ToListAsync(cancellationToken);
    }
}

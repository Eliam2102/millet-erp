using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.PublicPorts;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.PublicAdapters;
public sealed class OcImportacionReadAdapter(ComprasDbContext db) : IOcImportacionReadPort
{
    public async Task<OcImportacionLectura?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var oc = await db.OrdenesCompra.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return oc is null ? null : De(oc);
    }

    public async Task<IReadOnlyList<OcImportacionLectura>> ListarAsync(IReadOnlyList<Guid>? sucursales, string? q, CancellationToken ct)
    {
        var ocs = await CrearConsulta(db, sucursales, q).ToListAsync(ct);
        return ocs.Select(De).ToArray();
    }

    internal static IQueryable<OrdenCompra> CrearConsulta(ComprasDbContext db, IReadOnlyList<Guid>? sucursales, string? q)
    {
        // El conversor de Folio aplica también al patrón ILIKE de LINQ. El SQL parametrizado
        // filtra el texto nativo; EF sigue aplicando el filtro de empresa y la sucursal.
        var origen = string.IsNullOrWhiteSpace(q) ? db.OrdenesCompra : db.OrdenesCompra.FromSqlInterpolated($"SELECT * FROM compras.ordenes_compra WHERE folio ILIKE {"%" + q.Trim() + "%"}");
        var query = origen.AsNoTracking().Where(x => x.EsImportacion && x.Estado == EstadoOrdenCompra.Autorizada);
        if (sucursales is not null)
            query = query.Where(x => sucursales.Contains(x.SucursalDestinoId));
        return query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(50);
    }

    private static OcImportacionLectura De(OrdenCompra oc) => new(oc.Id, oc.Folio.Valor, oc.ProveedorId, oc.Moneda, oc.SucursalDestinoId, oc.Estado, oc.EsImportacion, oc.InformacionImportacion);
}

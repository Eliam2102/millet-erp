using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.PublicPorts;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.DatosMaestros;
namespace Millet.Compras.Infrastructure.PublicAdapters;

public sealed class OrdenesServicioReadAdapter(ComprasDbContext db, IProveedorReadPort proveedores) : IOrdenesServicioReadPort
{
    public async Task<OrdenServicioLectura?> ObtenerAsync(Guid id, CancellationToken ct)
    {
        var ordenes = await db.OrdenesCompra.AsNoTracking().Include(o => o.Lineas).Include(o => o.Adjuntos)
            .Where(o => o.Id == id).ToListAsync(ct);
        return (await MapearAsync(ordenes, ct)).SingleOrDefault();
    }
    public async Task<IReadOnlyList<OrdenServicioLectura>> BuscarAsync(Guid empresaId, Guid sucursalId, Guid? proveedorId, string? texto, CancellationToken ct)
    {
        return await MapearAsync(await CrearConsulta(db, empresaId, sucursalId, proveedorId, texto).ToListAsync(ct), ct);
    }
    internal static IQueryable<OrdenCompra> CrearConsulta(ComprasDbContext db, Guid empresaId, Guid sucursalId, Guid? proveedorId, string? texto)
    {
        // Folio es un value object: el patrón se compara en SQL antes de aplicar los filtros de EF.
        var origen = string.IsNullOrWhiteSpace(texto) ? db.OrdenesCompra : db.OrdenesCompra.FromSqlInterpolated($"SELECT * FROM compras.ordenes_compra WHERE folio ILIKE {"%" + texto.Trim() + "%"} OR observaciones ILIKE {"%" + texto.Trim() + "%"} OR id IN (SELECT orden_compra_id FROM compras.orden_compra_lineas WHERE descripcion_extendida ILIKE {"%" + texto.Trim() + "%"})");
        var query = origen.AsNoTracking().Include(o => o.Lineas).Include(o => o.Adjuntos)
            .Where(o => o.EmpresaId == empresaId && o.SucursalDestinoId == sucursalId && o.Estado != EstadoOrdenCompra.Cancelada && o.Lineas.Any(l => l.EsServicio));
        if (proveedorId is Guid proveedor) query = query.Where(o => o.ProveedorId == proveedor);
        return query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Take(50);
    }
    private async Task<IReadOnlyList<OrdenServicioLectura>> MapearAsync(List<OrdenCompra> ordenes, CancellationToken ct)
    {
        var tipos = await db.TiposDocumentoOc.Where(t => t.Clave == "cotizacion").Select(t => t.Id).ToArrayAsync(ct);
        var nombres = await proveedores.ObtenerPorIdsAsync(ordenes.Select(o => o.ProveedorId).Distinct().ToArray(), ct);
        return ordenes.Select(o => new OrdenServicioLectura(o.Id, o.EmpresaId, o.SucursalDestinoId, o.Folio.Valor,
            o.ProveedorId, nombres.GetValueOrDefault(o.ProveedorId)?.RazonSocial ?? "Proveedor de la orden", o.Moneda,
            o.CalcularTotales().TotalAPagar, o.Estado,
            o.Adjuntos.Where(a => tipos.Contains(a.TipoDocumentoId)).OrderByDescending(a => a.FechaCarga).Select(a => (Guid?)a.Id).FirstOrDefault(),
            o.InformacionImportacion?.NumeroContenedor,
            o.Lineas.Where(l => l.EsServicio).Select(l => new LineaServicioLectura(l.Id, l.DescripcionExtendida ?? "Servicio", l.SubtotalLinea + l.IvaImporte - (l.RetencionIsr ?? 0))).ToArray())).ToArray();
    }
}

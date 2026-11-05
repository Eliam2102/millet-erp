using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Ports;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Implementa <see cref="IPendientesMaestroReadPort"/> con las OC abiertas (ni Cancelada ni Cerrada) de un proveedor,
/// o con cantidad por recibir de un artículo. Maestros cross-empresa: ignora el filtro de empresa.
/// </summary>
public sealed class OcPendientesMaestroReadAdapter(ComprasDbContext db, ICurrentEmpresaContext empresa)
    : IPendientesMaestroReadPort
{
    public async Task<IReadOnlyList<string>> ObtenerAsync(TipoMaestro tipo, Guid id, CancellationToken cancellationToken)
    {
        if (tipo is not (TipoMaestro.Proveedor or TipoMaestro.Articulo)) return [];
        using var bypass = empresa.Bypass();
        var abiertas = db.OrdenesCompra.AsNoTracking()
            .Where(o => o.Estado != EstadoOrdenCompra.Cancelada && o.Estado != EstadoOrdenCompra.Cerrada);
        abiertas = tipo == TipoMaestro.Proveedor
            ? abiertas.Where(o => o.ProveedorId == id)
            : abiertas.Where(o => o.Lineas.Any(l => l.ArticuloId == id && l.CantidadRecibida < l.Cantidad));
        var ocs = await abiertas.OrderBy(o => o.FolioAnio).ThenBy(o => o.Folio)
            .Select(o => new { o.Folio, o.Estado }).ToListAsync(cancellationToken);
        return ocs.Select(o => $"OC {o.Folio} ({o.Estado}).").ToList();
    }
}

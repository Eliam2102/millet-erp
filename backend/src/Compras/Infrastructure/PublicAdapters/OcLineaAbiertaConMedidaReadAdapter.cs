using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Ports;
using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Implementa <see cref="IOcLineaAbiertaConMedidaReadPort"/> sobre <c>orden_compra_lineas</c>. El artículo es
/// cross-empresa, así que la consulta ignora el filtro de empresa.
/// </summary>
public sealed class OcLineaAbiertaConMedidaReadAdapter : IOcLineaAbiertaConMedidaReadPort
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public OcLineaAbiertaConMedidaReadAdapter(ComprasDbContext db, ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<bool> ArticuloTieneLineaAbiertaConMedidaAsync(Guid articuloId, CancellationToken cancellationToken)
    {
        using var bypass = _empresaContext.Bypass();
        return await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Estado != EstadoOrdenCompra.Cancelada && o.Estado != EstadoOrdenCompra.Cerrada)
            .SelectMany(o => o.Lineas)
            .AnyAsync(l => l.ArticuloId == articuloId
                           && l.ProductoAwVarianteId != null
                           && l.CantidadRecibida < l.Cantidad, cancellationToken);
    }
}

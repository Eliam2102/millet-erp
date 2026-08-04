using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.ListarUltimas100Compras;

public sealed class ListarUltimas100ComprasHandler
    : IRequestHandler<ListarUltimas100ComprasQuery, ListarUltimas100ComprasResponse>
{
    private const int Limite = 100;

    private readonly ComprasDbContext _db;

    public ListarUltimas100ComprasHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<ListarUltimas100ComprasResponse> Handle(
        ListarUltimas100ComprasQuery request, CancellationToken cancellationToken)
    {
        // Join Lineas → OrdenesCompra para filtrar y ordenar por
        // FechaDocumento de la OC. AsNoTracking + projection plana.
        var query = from linea in _db.LineasOrdenCompra.AsNoTracking()
                    join oc in _db.OrdenesCompra.AsNoTracking() on linea.OrdenCompraId equals oc.Id
                    where linea.ArticuloId == request.ArticuloId
                    select new { linea, oc };

        if (request.ProveedorId is Guid p)
        {
            query = query.Where(x => x.oc.ProveedorId == p);
        }
        if (request.FechaDesde is DateOnly desde)
        {
            query = query.Where(x => x.oc.FechaDocumento >= desde);
        }
        if (request.CantidadMinima is decimal qty)
        {
            query = query.Where(x => x.linea.Cantidad >= qty);
        }

        var items = await query
            .OrderByDescending(x => x.oc.FechaDocumento)
            .Take(Limite)
            .Select(x => new CompraMaterialResumen(
                x.linea.Id,
                x.oc.Id,
                x.oc.Folio.Valor,
                x.oc.ProveedorId,
                x.linea.Cantidad,
                x.linea.UnidadMedida,
                x.linea.PrecioUnitario,
                x.oc.Moneda,
                x.oc.FechaDocumento))
            .ToListAsync(cancellationToken);

        return new ListarUltimas100ComprasResponse(items);
    }
}

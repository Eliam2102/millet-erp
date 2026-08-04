using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Oc.ListarOrdenesCompra;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Oc.ListarHermanasDuplicadas;

public sealed class ListarHermanasDuplicadasHandler
    : IRequestHandler<ListarHermanasDuplicadasQuery, ListarHermanasDuplicadasResponse>
{
    private readonly ComprasDbContext _db;

    public ListarHermanasDuplicadasHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<ListarHermanasDuplicadasResponse> Handle(
        ListarHermanasDuplicadasQuery request, CancellationToken cancellationToken)
    {
        var hermanas = await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.OcOrigenId == request.OrdenCompraOrigenId)
            .OrderByDescending(o => o.FechaDocumento)
            .Select(o => new OrdenCompraResumen(
                o.Id,
                o.Folio.Valor,
                o.FolioAnio,
                o.Estado,
                o.SubEstadoRecepcion,
                o.SubEstadoFacturacion,
                o.SubEstadoPago,
                o.ProveedorId,
                // ProveedorNombre: solo lo enriquece ListarOrdenesCompra.
                (string?)null,
                o.SucursalDestinoId,
                o.CompradorTitularId,
                o.Moneda,
                o.FechaDocumento,
                o.ReferenciaProveedor))
            .ToListAsync(cancellationToken);

        return new ListarHermanasDuplicadasResponse(hermanas);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ObtenerOrdenCompraOrigen;

public sealed class ObtenerOrdenCompraOrigenHandler
    : IRequestHandler<ObtenerOrdenCompraOrigenQuery, ObtenerOrdenCompraOrigenResponse?>
{
    private readonly ComprasDbContext _db;

    public ObtenerOrdenCompraOrigenHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<ObtenerOrdenCompraOrigenResponse?> Handle(
        ObtenerOrdenCompraOrigenQuery request, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Id == request.OrdenCompraId)
            .Select(o => new { o.OcOrigenId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{request.OrdenCompraId}'.");

        if (oc.OcOrigenId is not Guid origenId)
        {
            return null;
        }

        var origen = await _db.OrdenesCompra
            .AsNoTracking()
            .Where(o => o.Id == origenId)
            .Select(o => new { o.Id, Folio = o.Folio.Valor })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(
                "OC_ORIGEN_NO_ENCONTRADA",
                $"La OC referencia un origen '{origenId}' que no existe.");

        return new ObtenerOrdenCompraOrigenResponse(origen.Id, origen.Folio);
    }
}

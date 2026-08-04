using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ActualizarReferenciaProveedor;

public sealed class ActualizarReferenciaProveedorHandler
    : IRequestHandler<ActualizarReferenciaProveedorCommand>
{
    private readonly ComprasDbContext _db;

    public ActualizarReferenciaProveedorHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task Handle(ActualizarReferenciaProveedorCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        oc.ActualizarReferenciaProveedor(command.ReferenciaProveedor);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

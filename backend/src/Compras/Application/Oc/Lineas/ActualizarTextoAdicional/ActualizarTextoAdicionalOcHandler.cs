using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Lineas.ActualizarTextoAdicional;

public sealed class ActualizarTextoAdicionalOcHandler
    : IRequestHandler<ActualizarTextoAdicionalOcCommand>
{
    private readonly ComprasDbContext _db;

    public ActualizarTextoAdicionalOcHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task Handle(ActualizarTextoAdicionalOcCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}' en la empresa actual.");

        oc.ActualizarTextoAdicionalLinea(command.LineaId, command.TextoAdicional);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

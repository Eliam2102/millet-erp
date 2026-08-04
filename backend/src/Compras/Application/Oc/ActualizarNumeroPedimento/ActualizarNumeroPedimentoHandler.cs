using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ActualizarNumeroPedimento;

public sealed class ActualizarNumeroPedimentoHandler
    : IRequestHandler<ActualizarNumeroPedimentoCommand>
{
    private readonly ComprasDbContext _db;

    public ActualizarNumeroPedimentoHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task Handle(ActualizarNumeroPedimentoCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        oc.ActualizarNumeroPedimento(command.NumeroPedimento);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Integration.AlmCompra;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ActualizarNumeroPedimento;

public sealed class ActualizarNumeroPedimentoHandler
    : IRequestHandler<ActualizarNumeroPedimentoCommand>
{
    private readonly ComprasDbContext _db;
    private readonly AlmCompraEncolador _almCompra;

    public ActualizarNumeroPedimentoHandler(ComprasDbContext db, AlmCompraEncolador almCompra)
    {
        _db = db;
        _almCompra = almCompra;
    }

    public async Task Handle(ActualizarNumeroPedimentoCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        oc.ActualizarNumeroPedimento(
            command.NumeroPedimento,
            command.NumeroBl,
            command.FechaEstimadaArribo,
            command.AgenteAduanalNaviera);
        // 86e3jvu83: el pedimento llega después por COMPRA_MODIFICADA (completa), en la misma transacción.
        await _almCompra.PedimentoActualizadoAsync(oc.Id, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

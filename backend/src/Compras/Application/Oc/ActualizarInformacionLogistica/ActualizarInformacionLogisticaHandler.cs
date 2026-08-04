using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ActualizarInformacionLogistica;

public sealed class ActualizarInformacionLogisticaHandler
    : IRequestHandler<ActualizarInformacionLogisticaCommand>
{
    private readonly ComprasDbContext _db;

    public ActualizarInformacionLogisticaHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task Handle(ActualizarInformacionLogisticaCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        var info = (command.DireccionEntrega, command.TransportistaId, command.TransportistaTexto,
                    command.NumeroGuia, command.InstruccionesEnvio) switch
        {
            (null, null, null, null, null) => (InformacionLogistica?)null,
            _ => new InformacionLogistica(
                command.DireccionEntrega,
                command.TransportistaId,
                command.TransportistaTexto,
                command.NumeroGuia,
                command.InstruccionesEnvio),
        };

        oc.ActualizarInformacionLogistica(info);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

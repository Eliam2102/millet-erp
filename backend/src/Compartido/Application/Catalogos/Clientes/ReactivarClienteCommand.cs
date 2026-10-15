using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// Reactiva un cliente dado de baja localmente (<c>Estatus = Activo</c>).
/// Idempotente: si ya está activo no hace nada. Un cliente bloqueado en A+W
/// (<c>BloqueadoPorAw</c>) no se reactiva localmente; el bloqueo aprobado de
/// A+W prevalece (V52) y se levanta cuando A+W lo desbloquee.
/// </summary>
public sealed record ReactivarClienteCommand(Guid ClienteId) : IRequest;

public sealed class ReactivarClienteHandler : IRequestHandler<ReactivarClienteCommand>
{
    private readonly CompartidoDbContext _db;

    public ReactivarClienteHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(ReactivarClienteCommand request, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CLIENTE_NO_ENCONTRADO",
                $"No existe cliente con id '{request.ClienteId}'.");

        if (cliente.BloqueadoPorAw)
        {
            throw new BusinessRuleException(
                "CLIENTE_BLOQUEADO_POR_AW",
                "El cliente está bloqueado en A+W; se reactivará cuando A+W lo desbloquee.");
        }

        if (cliente.Estatus != EstatusCatalogo.Activo)
        {
            cliente.CambiarEstatus(EstatusCatalogo.Activo);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}

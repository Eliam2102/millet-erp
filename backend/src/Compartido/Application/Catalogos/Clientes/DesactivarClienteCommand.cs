using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// Soft delete de cliente: <c>Estatus = Inactivo</c>. Idempotente. Los
/// pedidos/CFDIs históricos siguen referenciando el id; los nuevos pedidos
/// de un cliente inactivo caen a la bandeja de excepciones de ingesta.
/// </summary>
public sealed record DesactivarClienteCommand(Guid ClienteId) : IRequest;

public sealed class DesactivarClienteHandler : IRequestHandler<DesactivarClienteCommand>
{
    private readonly CompartidoDbContext _db;

    public DesactivarClienteHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(DesactivarClienteCommand request, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CLIENTE_NO_ENCONTRADO",
                $"No existe cliente con id '{request.ClienteId}'.");

        if (cliente.Estatus != EstatusCatalogo.Inactivo)
        {
            cliente.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}

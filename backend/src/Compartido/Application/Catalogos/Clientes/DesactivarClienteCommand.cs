using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.Clientes;

/// <summary>
/// Soft delete de cliente: <c>Estatus = Inactivo</c> con motivo obligatorio. Antes revisa lo abierto
/// (saldo por cobrar y pedidos sin facturar). Idempotente. Los pedidos/CFDIs históricos siguen
/// referenciando el id; los nuevos pedidos de un cliente inactivo caen a la bandeja de excepciones de ingesta.
/// </summary>
public sealed record DesactivarClienteCommand(Guid ClienteId, string Motivo) : IRequest;

public sealed class DesactivarClienteValidator : AbstractValidator<DesactivarClienteCommand>
{
    public DesactivarClienteValidator() => MaestroEstatusService.ReglaMotivo(RuleFor(x => x.Motivo));
}

public sealed class DesactivarClienteHandler : IRequestHandler<DesactivarClienteCommand>
{
    private readonly CompartidoDbContext _db;
    private readonly MaestroEstatusService _estatus;

    public DesactivarClienteHandler(CompartidoDbContext db, MaestroEstatusService estatus)
    {
        _db = db;
        _estatus = estatus;
    }

    public async Task Handle(DesactivarClienteCommand request, CancellationToken cancellationToken)
    {
        var cliente = await _db.Clientes
            .FirstOrDefaultAsync(c => c.Id == request.ClienteId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CLIENTE_NO_ENCONTRADO",
                $"No existe cliente con id '{request.ClienteId}'.");

        if (cliente.Estatus != EstatusCatalogo.Inactivo)
        {
            await _estatus.ExigirSinPendientesAsync(
                TipoMaestro.Cliente, cliente.Id, $"al cliente {cliente.Clave}", cancellationToken);

            cliente.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
            await _estatus.RegistrarAsync("cliente.inactivar", "Cliente", cliente.Id,
                $"{cliente.Clave} · {cliente.RazonSocial}", true, request.Motivo.Trim(), cancellationToken);
        }
    }
}

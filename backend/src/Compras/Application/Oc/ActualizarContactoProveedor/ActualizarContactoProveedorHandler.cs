using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.ActualizarContactoProveedor;

public sealed class ActualizarContactoProveedorHandler
    : IRequestHandler<ActualizarContactoProveedorCommand>
{
    private readonly ComprasDbContext _db;

    public ActualizarContactoProveedorHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task Handle(ActualizarContactoProveedorCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        // Si los 3 campos son null/vacíos, se limpia el snapshot.
        var contacto = (command.Nombre, command.Email, command.Telefono) switch
        {
            (null, null, null) => (ContactoProveedor?)null,
            var (n, e, t) when string.IsNullOrWhiteSpace(n) && string.IsNullOrWhiteSpace(e) && string.IsNullOrWhiteSpace(t)
                => (ContactoProveedor?)null,
            _ => new ContactoProveedor(command.Nombre, command.Email, command.Telefono),
        };

        oc.ActualizarContactoProveedor(contacto);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

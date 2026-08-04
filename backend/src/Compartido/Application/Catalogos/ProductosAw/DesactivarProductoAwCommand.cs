using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.ProductosAw;

/// <summary>
/// Soft delete de producto A+W: <c>Estatus = Inactivo</c>. Idempotente.
/// Un pedido entrante que referencia un producto inactivo cae a la bandeja
/// de excepciones de ingesta (no se re-provisiona en automático).
/// </summary>
public sealed record DesactivarProductoAwCommand(Guid ProductoAwId) : IRequest;

public sealed class DesactivarProductoAwHandler : IRequestHandler<DesactivarProductoAwCommand>
{
    private readonly CompartidoDbContext _db;

    public DesactivarProductoAwHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(DesactivarProductoAwCommand request, CancellationToken cancellationToken)
    {
        var producto = await _db.ProductosAw
            .FirstOrDefaultAsync(p => p.Id == request.ProductoAwId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PRODUCTO_AW_NO_ENCONTRADO",
                $"No existe producto A+W con id '{request.ProductoAwId}'.");

        if (producto.Estatus != EstatusCatalogo.Inactivo)
        {
            producto.CambiarEstatus(EstatusCatalogo.Inactivo);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}

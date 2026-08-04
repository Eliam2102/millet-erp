using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// Soft delete de proveedor (B.5): setea <c>Estatus = Inactivo</c>.
/// Idempotente: si ya está Inactivo, no-op (devuelve 204 sin cambio).
/// Las RQs históricas siguen funcionando con el id; las nuevas RQs
/// se bloquean en F7-PR1 cross-table validation.
/// </summary>
public sealed record DesactivarProveedorCommand(Guid ProveedorId) : IRequest;

public sealed class DesactivarProveedorHandler : IRequestHandler<DesactivarProveedorCommand>
{
    private readonly CompartidoDbContext _db;

    public DesactivarProveedorHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(DesactivarProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await _db.Proveedores
            .FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{request.ProveedorId}'.");

        if (proveedor.Estatus == EstatusCatalogo.Inactivo) return;

        proveedor.CambiarEstatus(EstatusCatalogo.Inactivo);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

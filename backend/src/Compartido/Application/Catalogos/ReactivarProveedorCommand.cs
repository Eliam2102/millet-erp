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

/// <summary>Reactiva el registro. Si ya está activo, no escribe cambios.</summary>
public sealed record ReactivarProveedorCommand(Guid ProveedorId) : IRequest;

public sealed class ReactivarProveedorHandler : IRequestHandler<ReactivarProveedorCommand>
{
    private readonly CompartidoDbContext _db;

    public ReactivarProveedorHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(ReactivarProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await _db.Proveedores
            .FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{request.ProveedorId}'.");

        if (proveedor.Estatus == EstatusCatalogo.Activo) return;
        if (proveedor.Estatus != EstatusCatalogo.Inactivo)
            throw new BusinessRuleException("PROVEEDOR_NO_INACTIVO", "Sólo se puede reactivar un proveedor inactivo. Los proveedores en revisión deben pasar por validación.");

        proveedor.CambiarEstatus(EstatusCatalogo.Activo);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

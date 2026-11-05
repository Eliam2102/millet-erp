using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>Reactiva el registro con motivo obligatorio. Si ya está activo, no escribe cambios.</summary>
public sealed record ReactivarProveedorCommand(Guid ProveedorId, string Motivo) : IRequest;

public sealed class ReactivarProveedorValidator : AbstractValidator<ReactivarProveedorCommand>
{
    public ReactivarProveedorValidator() => MaestroEstatusService.ReglaMotivo(RuleFor(x => x.Motivo));
}

public sealed class ReactivarProveedorHandler : IRequestHandler<ReactivarProveedorCommand>
{
    private readonly CompartidoDbContext _db;
    private readonly MaestroEstatusService _estatus;

    public ReactivarProveedorHandler(CompartidoDbContext db, MaestroEstatusService estatus)
    {
        _db = db;
        _estatus = estatus;
    }

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
        await _estatus.RegistrarAsync("proveedor.reactivar", "Proveedor", proveedor.Id,
            $"{proveedor.Clave} · {proveedor.RazonSocial}", false, request.Motivo.Trim(), cancellationToken);
    }
}

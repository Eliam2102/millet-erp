using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// Soft delete de proveedor (B.5): setea <c>Estatus = Inactivo</c> con motivo obligatorio.
/// Antes revisa lo abierto (OC y saldo por pagar) y, si hay algo, no deja inactivar.
/// Idempotente: si ya está Inactivo, no-op (devuelve 204 sin cambio).
/// Las RQs históricas siguen funcionando con el id; las nuevas RQs
/// se bloquean en F7-PR1 cross-table validation.
/// </summary>
public sealed record DesactivarProveedorCommand(Guid ProveedorId, string Motivo) : IRequest;

public sealed class DesactivarProveedorValidator : AbstractValidator<DesactivarProveedorCommand>
{
    public DesactivarProveedorValidator() => MaestroEstatusService.ReglaMotivo(RuleFor(x => x.Motivo));
}

public sealed class DesactivarProveedorHandler : IRequestHandler<DesactivarProveedorCommand>
{
    private readonly CompartidoDbContext _db;
    private readonly MaestroEstatusService _estatus;

    public DesactivarProveedorHandler(CompartidoDbContext db, MaestroEstatusService estatus)
    {
        _db = db;
        _estatus = estatus;
    }

    public async Task Handle(DesactivarProveedorCommand request, CancellationToken cancellationToken)
    {
        var proveedor = await _db.Proveedores
            .FirstOrDefaultAsync(p => p.Id == request.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PROVEEDOR_NO_ENCONTRADO",
                $"No se encontró proveedor con id '{request.ProveedorId}'.");

        if (proveedor.Estatus == EstatusCatalogo.Inactivo) return;

        await _estatus.ExigirSinPendientesAsync(
            TipoMaestro.Proveedor, proveedor.Id, $"al proveedor {proveedor.Clave}", cancellationToken);

        proveedor.CambiarEstatus(EstatusCatalogo.Inactivo);
        await _db.SaveChangesAsync(cancellationToken);
        await _estatus.RegistrarAsync("proveedor.inactivar", "Proveedor", proveedor.Id,
            $"{proveedor.Clave} · {proveedor.RazonSocial}", true, request.Motivo.Trim(), cancellationToken);
    }
}

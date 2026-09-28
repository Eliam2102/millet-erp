using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Sucursales;

/// <summary>
/// Desactiva una sucursal (Estatus = Inactivo) (F-Admin-PR2.3).
/// Idempotente: si ya está inactiva, no-op.
/// </summary>
public sealed record DesactivarSucursalCommand(Guid Id) : IRequest<SucursalResponse>;

public sealed class DesactivarSucursalHandler
    : IRequestHandler<DesactivarSucursalCommand, SucursalResponse>
{
    private readonly CompartidoDbContext _db;

    public DesactivarSucursalHandler(CompartidoDbContext db) => _db = db;

    public async Task<SucursalResponse> Handle(
        DesactivarSucursalCommand command, CancellationToken cancellationToken)
    {
        var sucursal = await _db.Sucursales
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{command.Id}'.");

        if (sucursal.Estatus != EstatusCatalogo.Inactivo)
        {
            sucursal.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new SucursalResponse(
            sucursal.Id, sucursal.Clave, sucursal.Nombre, sucursal.Tipo,
            sucursal.Estatus, sucursal.Version, sucursal.ClaveAw, sucursal.ZonaHoraria);
    }
}

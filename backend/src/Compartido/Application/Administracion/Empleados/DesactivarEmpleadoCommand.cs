using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empleados;

/// <summary>
/// Desactiva un empleado (baja; Estatus = Inactivo) (ADM-PR1).
/// Idempotente. Las referencias históricas (viáticos, comprobaciones,
/// subordinados con este jefe directo) se conservan.
/// </summary>
public sealed record DesactivarEmpleadoCommand(Guid Id) : IRequest<EmpleadoResponse>;

public sealed class DesactivarEmpleadoHandler
    : IRequestHandler<DesactivarEmpleadoCommand, EmpleadoResponse>
{
    private readonly CompartidoDbContext _db;

    public DesactivarEmpleadoHandler(CompartidoDbContext db) => _db = db;

    public async Task<EmpleadoResponse> Handle(
        DesactivarEmpleadoCommand command, CancellationToken cancellationToken)
    {
        var empleado = await _db.Empleados
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPLEADO_NO_ENCONTRADO",
                $"No existe empleado con id '{command.Id}'.");

        if (empleado.Estatus != EstatusCatalogo.Inactivo)
        {
            empleado.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return CrearEmpleadoHandler.Mapear(empleado);
    }
}

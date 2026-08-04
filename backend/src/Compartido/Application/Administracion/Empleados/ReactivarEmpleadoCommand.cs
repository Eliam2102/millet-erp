using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empleados;

/// <summary>
/// Reactiva un empleado (recontratación; Estatus = Activo) (ADM-FE-PR1 —
/// contraparte de <see cref="DesactivarEmpleadoCommand"/> para el toggle
/// del admin UI). Idempotente.
/// </summary>
public sealed record ReactivarEmpleadoCommand(Guid Id) : IRequest<EmpleadoResponse>;

public sealed class ReactivarEmpleadoHandler
    : IRequestHandler<ReactivarEmpleadoCommand, EmpleadoResponse>
{
    private readonly CompartidoDbContext _db;

    public ReactivarEmpleadoHandler(CompartidoDbContext db) => _db = db;

    public async Task<EmpleadoResponse> Handle(
        ReactivarEmpleadoCommand command, CancellationToken cancellationToken)
    {
        var empleado = await _db.Empleados
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPLEADO_NO_ENCONTRADO",
                $"No existe empleado con id '{command.Id}'.");

        if (empleado.Estatus != EstatusCatalogo.Activo)
        {
            empleado.Activar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return CrearEmpleadoHandler.Mapear(empleado);
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Puestos;

/// <summary>
/// Desactiva un puesto (Estatus = Inactivo) (ADM-PR1). Idempotente.
/// Los empleados que lo referencian conservan la FK (histórico); las
/// políticas de viáticos sobre un puesto inactivo dejan de aplicar a
/// solicitudes nuevas cuando CxP valida el estatus vía read port.
/// </summary>
public sealed record DesactivarPuestoCommand(Guid Id) : IRequest<PuestoResponse>;

public sealed class DesactivarPuestoHandler
    : IRequestHandler<DesactivarPuestoCommand, PuestoResponse>
{
    private readonly CompartidoDbContext _db;

    public DesactivarPuestoHandler(CompartidoDbContext db) => _db = db;

    public async Task<PuestoResponse> Handle(
        DesactivarPuestoCommand command, CancellationToken cancellationToken)
    {
        var puesto = await _db.Puestos
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PUESTO_NO_ENCONTRADO",
                $"No existe puesto con id '{command.Id}'.");

        if (puesto.Estatus != EstatusCatalogo.Inactivo)
        {
            puesto.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new PuestoResponse(
            puesto.Id, puesto.Clave, puesto.Nombre, puesto.Estatus, puesto.Version);
    }
}

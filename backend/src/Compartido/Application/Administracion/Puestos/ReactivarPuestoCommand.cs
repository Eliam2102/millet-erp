using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Puestos;

/// <summary>
/// Reactiva un puesto (Estatus = Activo) (ADM-FE-PR1 — contraparte de
/// <see cref="DesactivarPuestoCommand"/> para el toggle del admin UI).
/// Idempotente.
/// </summary>
public sealed record ReactivarPuestoCommand(Guid Id) : IRequest<PuestoResponse>;

public sealed class ReactivarPuestoHandler
    : IRequestHandler<ReactivarPuestoCommand, PuestoResponse>
{
    private readonly CompartidoDbContext _db;

    public ReactivarPuestoHandler(CompartidoDbContext db) => _db = db;

    public async Task<PuestoResponse> Handle(
        ReactivarPuestoCommand command, CancellationToken cancellationToken)
    {
        var puesto = await _db.Puestos
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PUESTO_NO_ENCONTRADO",
                $"No existe puesto con id '{command.Id}'.");

        if (puesto.Estatus != EstatusCatalogo.Activo)
        {
            puesto.Activar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new PuestoResponse(
            puesto.Id, puesto.Clave, puesto.Nombre, puesto.Estatus, puesto.Version);
    }
}

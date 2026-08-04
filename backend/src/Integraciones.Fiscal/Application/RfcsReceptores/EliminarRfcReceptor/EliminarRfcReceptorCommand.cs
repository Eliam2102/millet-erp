using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.Application.RfcsReceptores.EliminarRfcReceptor;

/// <summary>
/// Soft-delete del RfcReceptor. El query filter del BaseDbContext
/// excluye automáticamente las filas con <c>DeletedAt</c> != null,
/// pero el checkpoint persiste para futura re-alta con el mismo RFC
/// (manual review primero — la UNIQUE seguirá rechazando, hay que
/// limpiar el deleted_at).
/// </summary>
public sealed record EliminarRfcReceptorCommand(Guid Id) : IRequest;

public sealed class EliminarRfcReceptorHandler : IRequestHandler<EliminarRfcReceptorCommand>
{
    private readonly IntegracionesFiscalDbContext _db;

    public EliminarRfcReceptorHandler(IntegracionesFiscalDbContext db)
    {
        _db = db;
    }

    public async Task Handle(EliminarRfcReceptorCommand command, CancellationToken cancellationToken)
    {
        var entity = await _db.RfcsReceptores
            .FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "RFC_RECEPTOR_NO_ENCONTRADO",
                $"No existe el RFC receptor {command.Id}.");

        _db.RfcsReceptores.Remove(entity); // BaseDbContext convierte a soft-delete
        await _db.SaveChangesAsync(cancellationToken);
    }
}

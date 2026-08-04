using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Lineas.ActualizarNotasLinea;

public sealed class ActualizarNotasLineaHandler : IRequestHandler<ActualizarNotasLineaCommand, Unit>
{
    private readonly ComprasDbContext _db;

    public ActualizarNotasLineaHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<Unit> Handle(ActualizarNotasLineaCommand command, CancellationToken cancellationToken)
    {
        var requisicion = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        requisicion.ActualizarLineaNotas(command.LineaId, command.Notas);

        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

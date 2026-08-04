using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

/// <summary>
/// Desactiva una <see cref="Domain.Serie"/> (F-Admin-PR6.1). Idempotente:
/// si ya está inactiva, no-op.
/// </summary>
public sealed record DesactivarSerieCommand(Guid Id) : IRequest<SerieResponse>;

public sealed class DesactivarSerieHandler
    : IRequestHandler<DesactivarSerieCommand, SerieResponse>
{
    private readonly CompartidoDbContext _db;

    public DesactivarSerieHandler(CompartidoDbContext db) => _db = db;

    public async Task<SerieResponse> Handle(
        DesactivarSerieCommand command, CancellationToken cancellationToken)
    {
        var serie = await _db.Series
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SERIE_NO_ENCONTRADA",
                $"No existe serie con id '{command.Id}'.");

        if (serie.Activa)
        {
            serie.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return CrearSerieHandler.Map(serie);
    }
}

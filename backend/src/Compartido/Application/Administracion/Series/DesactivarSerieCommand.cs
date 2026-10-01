using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

/// <summary>
/// Desactiva una <see cref="Domain.Serie"/> (F-Admin-PR6.1). Idempotente:
/// si ya está inactiva, no-op.
/// </summary>
public sealed record DesactivarSerieCommand(Guid Id, int VersionEsperada) : IRequest<SerieResponse>;

public sealed class DesactivarSerieHandler
    : IRequestHandler<DesactivarSerieCommand, SerieResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly SerieSucursalScope _scope;

    public DesactivarSerieHandler(CompartidoDbContext db, SerieSucursalScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<SerieResponse> Handle(
        DesactivarSerieCommand command, CancellationToken cancellationToken)
    {
        var serie = await _db.Series
            .FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SERIE_NO_ENCONTRADA",
                $"No existe serie con id '{command.Id}'.");

        await _scope.VerificarAsync(serie.SucursalId, cancellationToken);

        if (serie.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Domain.Serie), serie.Id);

        if (serie.Activa)
        {
            serie.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return CrearSerieHandler.Map(serie);
    }
}

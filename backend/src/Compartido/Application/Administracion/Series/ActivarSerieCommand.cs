using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Series;

/// <summary>Reactiva una serie conservando sus secuencias y folios emitidos.</summary>
public sealed record ActivarSerieCommand(Guid Id, int VersionEsperada) : IRequest<SerieResponse>;

public sealed class ActivarSerieHandler : IRequestHandler<ActivarSerieCommand, SerieResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly SerieSucursalScope _scope;

    public ActivarSerieHandler(CompartidoDbContext db, SerieSucursalScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<SerieResponse> Handle(ActivarSerieCommand command, CancellationToken cancellationToken)
    {
        var serie = await _db.Series.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("SERIE_NO_ENCONTRADA", $"No existe serie con id '{command.Id}'.");
        await _scope.VerificarAsync(serie.SucursalId, cancellationToken);
        if (serie.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(Serie), serie.Id);

        if (!serie.Activa)
        {
            if (Serie.EsFiscal(serie.TipoDocumento) && await _db.Series.AsNoTracking().AnyAsync(
                s => s.Id != serie.Id && s.EmpresaId == serie.EmpresaId && s.SucursalId == serie.SucursalId
                    && s.TipoDocumento == serie.TipoDocumento && s.Activa, cancellationToken))
                throw new ConflictException("SERIE_ACTIVA_DUPLICADA",
                    "Ya existe otra serie fiscal activa para esa sucursal y tipo. Desactívala antes de reactivar esta serie.");

            serie.Activar();
            try { await _db.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException
                { SqlState: "23505", ConstraintName: "ix_series_fiscal_activa_sucursal" or "ix_series_fiscal_activa_global" })
            {
                throw new ConflictException("SERIE_ACTIVA_DUPLICADA",
                    "Otra solicitud activó una serie fiscal para esa sucursal y tipo.");
            }
        }
        return CrearSerieHandler.Map(serie);
    }
}

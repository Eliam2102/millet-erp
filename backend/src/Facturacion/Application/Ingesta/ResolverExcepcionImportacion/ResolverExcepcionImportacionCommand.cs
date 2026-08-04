using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Ingesta.ResolverExcepcionImportacion;

/// <summary>Marca una excepción de importación como resuelta (§12.1).</summary>
public sealed record ResolverExcepcionImportacionCommand(Guid Id) : IRequest<ResolverExcepcionImportacionResponse>;

public sealed record ResolverExcepcionImportacionResponse(Guid Id, bool Resuelto);

public sealed class ResolverExcepcionImportacionHandler
    : IRequestHandler<ResolverExcepcionImportacionCommand, ResolverExcepcionImportacionResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly ICurrentUserContext _user;
    private readonly IClock _clock;

    public ResolverExcepcionImportacionHandler(FacturacionDbContext db, ICurrentUserContext user, IClock clock)
    {
        _db = db;
        _user = user;
        _clock = clock;
    }

    public async Task<ResolverExcepcionImportacionResponse> Handle(
        ResolverExcepcionImportacionCommand command,
        CancellationToken cancellationToken)
    {
        var excepcion = await _db.ExcepcionesImportacion
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("EXCEPCION_NO_ENCONTRADA", $"No existe la excepción '{command.Id}'.");

        excepcion.Resolver(_user.UserId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return new ResolverExcepcionImportacionResponse(excepcion.Id, excepcion.Resuelto);
    }
}

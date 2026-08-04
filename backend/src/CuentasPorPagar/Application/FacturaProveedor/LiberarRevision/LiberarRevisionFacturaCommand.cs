using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.LiberarRevision;

public sealed record LiberarRevisionFacturaCommand(
    Guid Id,
    int VersionEsperada,
    string AccionTomada) : IRequest<LiberarRevisionFacturaResponse>;

public sealed record LiberarRevisionFacturaResponse(
    Guid Id,
    EstadoPasivo Estado,
    int Version);

public sealed class LiberarRevisionFacturaValidator : AbstractValidator<LiberarRevisionFacturaCommand>
{
    public LiberarRevisionFacturaValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.VersionEsperada).GreaterThanOrEqualTo(0);
        RuleFor(c => c.AccionTomada).NotEmpty().MaximumLength(400);
    }
}

public sealed class LiberarRevisionFacturaHandler : IRequestHandler<LiberarRevisionFacturaCommand, LiberarRevisionFacturaResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public LiberarRevisionFacturaHandler(CuentasPorPagarDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db; _currentUser = currentUser; _clock = clock;
    }

    public async Task<LiberarRevisionFacturaResponse> Handle(LiberarRevisionFacturaCommand command, CancellationToken cancellationToken)
    {
        var factura = await _db.FacturasProveedor
            .FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_NO_ENCONTRADA",
                $"No se encontró la factura con id '{command.Id}'.");

        if (factura.Version != command.VersionEsperada)
        {
            throw new ConcurrencyException(nameof(FacturaProveedor), factura.Id);
        }

        factura.LiberarRevision(command.AccionTomada, _currentUser.UserId, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return new LiberarRevisionFacturaResponse(factura.Id, factura.Estado, factura.Version);
    }
}

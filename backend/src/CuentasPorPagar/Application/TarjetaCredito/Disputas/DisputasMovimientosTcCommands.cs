using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.TarjetaCredito.Movimientos;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.TarjetaCredito.Disputas;

// ============================================================================
// F7-PR6: gestión de disputas (§8.5 anexo). El estado dentro del ERP;
// la gestión externa con el banco vive fuera.
// ============================================================================

public sealed record DisputarMovimientoTcCommand(
    Guid Id,
    int VersionEsperada,
    string Motivo,
    DateOnly FechaInicio) : IRequest<MovimientoTcResponse>;

public sealed class DisputarMovimientoTcValidator : AbstractValidator<DisputarMovimientoTcCommand>
{
    public DisputarMovimientoTcValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(1000);
    }
}

public sealed class DisputarMovimientoTcHandler
    : IRequestHandler<DisputarMovimientoTcCommand, MovimientoTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public DisputarMovimientoTcHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<MovimientoTcResponse> Handle(
        DisputarMovimientoTcCommand command, CancellationToken cancellationToken)
    {
        var mov = await _db.MovimientosTarjetaCredito
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("TC_MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento '{command.Id}'.");
        if (mov.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(MovimientoTarjetaCredito), mov.Id);

        mov.Disputar(command.Motivo, command.FechaInicio);
        await _db.SaveChangesAsync(cancellationToken);
        return MovimientoTcMapper.ToResponse(mov);
    }
}

// ----------------------------- Resolver disputa

public sealed record ResolverDisputaMovimientoTcCommand(
    Guid Id, int VersionEsperada, bool FueLegitimo) : IRequest<MovimientoTcResponse>;

public sealed class ResolverDisputaMovimientoTcValidator
    : AbstractValidator<ResolverDisputaMovimientoTcCommand>
{
    public ResolverDisputaMovimientoTcValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
    }
}

public sealed class ResolverDisputaMovimientoTcHandler
    : IRequestHandler<ResolverDisputaMovimientoTcCommand, MovimientoTcResponse>
{
    private readonly CuentasPorPagarDbContext _db;
    public ResolverDisputaMovimientoTcHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<MovimientoTcResponse> Handle(
        ResolverDisputaMovimientoTcCommand command, CancellationToken cancellationToken)
    {
        var mov = await _db.MovimientosTarjetaCredito
            .FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException("TC_MOV_NO_ENCONTRADO",
                $"No se encontró el movimiento '{command.Id}'.");
        if (mov.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(MovimientoTarjetaCredito), mov.Id);

        mov.ResolverDisputa(command.FueLegitimo);
        await _db.SaveChangesAsync(cancellationToken);
        return MovimientoTcMapper.ToResponse(mov);
    }
}

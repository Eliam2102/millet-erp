using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.MarcarCfdiDuplicado;

/// <summary>
/// Marca un CFDI como duplicado de otro previamente ingresado (F1-PR1).
/// Útil cuando el Auxiliar detecta que el mismo proveedor envió dos
/// XMLs con UUIDs distintos pero contenido equivalente (re-emisión por
/// el propio proveedor sin cancelar primero) — caso raro, manual.
/// </summary>
public sealed record MarcarCfdiDuplicadoCommand(Guid Id, Guid CfdiOriginalId) : IRequest<Unit>;

public sealed class MarcarCfdiDuplicadoValidator : AbstractValidator<MarcarCfdiDuplicadoCommand>
{
    public MarcarCfdiDuplicadoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.CfdiOriginalId).NotEmpty();
        RuleFor(c => c.CfdiOriginalId).NotEqual(c => c.Id)
            .WithMessage("Un CFDI no puede marcarse duplicado de sí mismo.");
    }
}

public sealed class MarcarCfdiDuplicadoHandler : IRequestHandler<MarcarCfdiDuplicadoCommand, Unit>
{
    private readonly CuentasPorPagarDbContext _db;

    public MarcarCfdiDuplicadoHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<Unit> Handle(MarcarCfdiDuplicadoCommand command, CancellationToken cancellationToken)
    {
        var cfdi = await _db.CfdisRecibidos
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CFDI_NO_ENCONTRADO",
                $"No existe un CFDI recibido con id '{command.Id}'.");

        var original = await _db.CfdisRecibidos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == command.CfdiOriginalId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CFDI_ORIGINAL_NO_ENCONTRADO",
                $"No existe el CFDI original con id '{command.CfdiOriginalId}'.");

        cfdi.MarcarDuplicado(original.Id);
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

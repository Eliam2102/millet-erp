using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.DescartarCfdi;

public sealed record DescartarCfdiCommand(Guid Id, string Motivo) : IRequest<Unit>;

public sealed class DescartarCfdiValidator : AbstractValidator<DescartarCfdiCommand>
{
    public DescartarCfdiValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Motivo).NotEmpty().MaximumLength(400);
    }
}

public sealed class DescartarCfdiHandler : IRequestHandler<DescartarCfdiCommand, Unit>
{
    private readonly CuentasPorPagarDbContext _db;

    public DescartarCfdiHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<Unit> Handle(DescartarCfdiCommand command, CancellationToken cancellationToken)
    {
        var cfdi = await _db.CfdisRecibidos
            .FirstOrDefaultAsync(c => c.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CFDI_NO_ENCONTRADO",
                $"No existe un CFDI recibido con id '{command.Id}'.");

        cfdi.Descartar(command.Motivo);
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

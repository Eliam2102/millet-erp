using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Movimientos;

public sealed record ReclasificarMovimientoCommand(Guid MovimientoId, Guid ConceptoId, string Motivo, int VersionEsperada) : IRequest;
public sealed class ReclasificarMovimientoValidator : AbstractValidator<ReclasificarMovimientoCommand>
{
    public ReclasificarMovimientoValidator()
    {
        RuleFor(x => x.MovimientoId).NotEmpty();
        RuleFor(x => x.ConceptoId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty().MaximumLength(400);
    }
}
public sealed class ReclasificarMovimientoHandler(TesoreriaDbContext db) : IRequestHandler<ReclasificarMovimientoCommand>
{
    public async Task Handle(ReclasificarMovimientoCommand command, CancellationToken cancellationToken)
    {
        var movimiento = await db.MovimientosBancarios.FirstOrDefaultAsync(m => m.Id == command.MovimientoId, cancellationToken)
            ?? throw new EntityNotFoundException("MOV_NO_ENCONTRADO", "No se encontró el movimiento.");
        if (movimiento.Version != command.VersionEsperada)
            throw new ConcurrencyException(nameof(movimiento), movimiento.Id);
        if (!await db.ConceptosMovimiento.AnyAsync(c => c.Id == command.ConceptoId && c.Activo, cancellationToken))
            throw new BusinessRuleException("MOV_CONCEPTO_INVALIDO", "Selecciona un concepto activo.");
        movimiento.Reclasificar(command.ConceptoId, command.Motivo);
        await db.SaveChangesAsync(cancellationToken);
    }
}

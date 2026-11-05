using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Devolver;

/// <summary>
/// Devuelve a corregir una requisición <c>EnAutorizacion</c> → <c>Borrador</c>
/// con comentario obligatorio. No cierra la requisición ni borra firmas
/// (quedan como historia). El permiso se valida en el endpoint Api.
/// </summary>
public sealed record DevolverRequisicionCommand(Guid RequisicionId, string Comentario) : IRequest<Unit>;

public sealed class DevolverRequisicionValidator : AbstractValidator<DevolverRequisicionCommand>
{
    public DevolverRequisicionValidator()
    {
        RuleFor(c => c.RequisicionId).NotEmpty().WithErrorCode("REQUISICION_REQUERIDA");
        RuleFor(c => c.Comentario).NotEmpty().WithErrorCode("DEVOLVER_COMENTARIO_REQUERIDO")
            .MaximumLength(500).WithErrorCode("DEVOLVER_COMENTARIO_DEMASIADO_LARGO");
    }
}

public sealed class DevolverRequisicionHandler : IRequestHandler<DevolverRequisicionCommand, Unit>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public DevolverRequisicionHandler(ComprasDbContext db, ICurrentUserContext currentUser, IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Unit> Handle(DevolverRequisicionCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid actorId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        var requisicion = await _db.Requisiciones
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        requisicion.Devolver(actorId, command.Comentario, _clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

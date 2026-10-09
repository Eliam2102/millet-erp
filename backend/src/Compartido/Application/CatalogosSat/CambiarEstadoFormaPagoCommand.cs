using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.Application.CatalogosSat;

public sealed record CambiarEstadoFormaPagoCommand(Guid Id, bool Activa) : IRequest<Unit>;
public sealed class CambiarEstadoFormaPagoValidator : AbstractValidator<CambiarEstadoFormaPagoCommand>
{
    public CambiarEstadoFormaPagoValidator() => RuleFor(x => x.Id).NotEmpty();
}
public sealed class CambiarEstadoFormaPagoHandler(CompartidoDbContext db)
    : IRequestHandler<CambiarEstadoFormaPagoCommand, Unit>
{
    public async Task<Unit> Handle(CambiarEstadoFormaPagoCommand request, CancellationToken cancellationToken)
    {
        var forma = await db.FormasPago.FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("FORMA_PAGO_NO_ENCONTRADA", "No existe la forma de pago SAT solicitada.");
        forma.CambiarEstado(request.Activa);
        await db.SaveChangesAsync(cancellationToken); // IAuditable: registra el cambio sin eliminar la clave SAT.
        return Unit.Value;
    }
}

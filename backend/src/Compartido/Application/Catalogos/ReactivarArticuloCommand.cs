using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>Reactiva el registro con motivo obligatorio. Si ya está activo, no escribe cambios.</summary>
public sealed record ReactivarArticuloCommand(Guid ArticuloId, string Motivo) : IRequest;

public sealed class ReactivarArticuloValidator : AbstractValidator<ReactivarArticuloCommand>
{
    public ReactivarArticuloValidator() => MaestroEstatusService.ReglaMotivo(RuleFor(x => x.Motivo));
}

public sealed class ReactivarArticuloHandler : IRequestHandler<ReactivarArticuloCommand>
{
    private readonly CompartidoDbContext _db;
    private readonly MaestroEstatusService _estatus;

    public ReactivarArticuloHandler(CompartidoDbContext db, MaestroEstatusService estatus)
    {
        _db = db;
        _estatus = estatus;
    }

    public async Task Handle(ReactivarArticuloCommand request, CancellationToken cancellationToken)
    {
        var articulo = await _db.Articulos
            .FirstOrDefaultAsync(a => a.Id == request.ArticuloId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ARTICULO_NO_ENCONTRADO",
                $"No se encontró artículo con id '{request.ArticuloId}'.");

        if (articulo.Estatus == EstatusCatalogo.Activo) return;

        articulo.CambiarEstatus(EstatusCatalogo.Activo);
        await _db.SaveChangesAsync(cancellationToken);
        await _estatus.RegistrarAsync("articulo.reactivar", "Articulo", articulo.Id,
            $"{articulo.Clave} · {articulo.Nombre}", false, request.Motivo.Trim(), cancellationToken);
    }
}

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos;
using Millet.Compartido.Application.Ports;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// Soft delete de artículo (B.5): setea <c>Estatus = Inactivo</c> con motivo obligatorio.
/// Antes revisa lo abierto (OC con cantidad por recibir y existencia en almacén).
/// Idempotente. Las RQs históricas siguen funcionando con el id;
/// las nuevas se bloquean en F7-PR1 cross-table validation.
/// </summary>
public sealed record DesactivarArticuloCommand(Guid ArticuloId, string Motivo) : IRequest;

public sealed class DesactivarArticuloValidator : AbstractValidator<DesactivarArticuloCommand>
{
    public DesactivarArticuloValidator() => MaestroEstatusService.ReglaMotivo(RuleFor(x => x.Motivo));
}

public sealed class DesactivarArticuloHandler : IRequestHandler<DesactivarArticuloCommand>
{
    private readonly CompartidoDbContext _db;
    private readonly MaestroEstatusService _estatus;

    public DesactivarArticuloHandler(CompartidoDbContext db, MaestroEstatusService estatus)
    {
        _db = db;
        _estatus = estatus;
    }

    public async Task Handle(DesactivarArticuloCommand request, CancellationToken cancellationToken)
    {
        var articulo = await _db.Articulos
            .FirstOrDefaultAsync(a => a.Id == request.ArticuloId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ARTICULO_NO_ENCONTRADO",
                $"No se encontró artículo con id '{request.ArticuloId}'.");

        if (articulo.Estatus == EstatusCatalogo.Inactivo) return;

        await _estatus.ExigirSinPendientesAsync(
            TipoMaestro.Articulo, articulo.Id, $"el artículo {articulo.Clave}", cancellationToken);

        articulo.CambiarEstatus(EstatusCatalogo.Inactivo);
        await _db.SaveChangesAsync(cancellationToken);
        await _estatus.RegistrarAsync("articulo.inactivar", "Articulo", articulo.Id,
            $"{articulo.Clave} · {articulo.Nombre}", true, request.Motivo.Trim(), cancellationToken);
    }
}

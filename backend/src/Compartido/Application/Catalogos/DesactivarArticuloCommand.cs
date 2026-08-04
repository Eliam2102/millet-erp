using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.DatosMaestros.Application.Catalogos;

/// <summary>
/// Soft delete de artículo (B.5): setea <c>Estatus = Inactivo</c>.
/// Idempotente. Las RQs históricas siguen funcionando con el id;
/// las nuevas se bloquean en F7-PR1 cross-table validation.
/// </summary>
public sealed record DesactivarArticuloCommand(Guid ArticuloId) : IRequest;

public sealed class DesactivarArticuloHandler : IRequestHandler<DesactivarArticuloCommand>
{
    private readonly CompartidoDbContext _db;

    public DesactivarArticuloHandler(CompartidoDbContext db) => _db = db;

    public async Task Handle(DesactivarArticuloCommand request, CancellationToken cancellationToken)
    {
        var articulo = await _db.Articulos
            .FirstOrDefaultAsync(a => a.Id == request.ArticuloId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ARTICULO_NO_ENCONTRADO",
                $"No se encontró artículo con id '{request.ArticuloId}'.");

        if (articulo.Estatus == EstatusCatalogo.Inactivo) return;

        articulo.CambiarEstatus(EstatusCatalogo.Inactivo);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

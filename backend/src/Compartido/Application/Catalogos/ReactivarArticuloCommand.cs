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

/// <summary>Reactiva el registro. Si ya está activo, no escribe cambios.</summary>
public sealed record ReactivarArticuloCommand(Guid ArticuloId) : IRequest;

public sealed class ReactivarArticuloHandler : IRequestHandler<ReactivarArticuloCommand>
{
    private readonly CompartidoDbContext _db;

    public ReactivarArticuloHandler(CompartidoDbContext db) => _db = db;

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
    }
}

using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Ports.Blob;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Adjuntos.RemoverAdjunto;

public sealed class RemoverAdjuntoHandler : IRequestHandler<RemoverAdjuntoCommand>
{
    private readonly ComprasDbContext _db;
    private readonly IAlmacenarBlobPort _blob;
    private readonly ILogger<RemoverAdjuntoHandler> _logger;

    public RemoverAdjuntoHandler(
        ComprasDbContext db,
        IAlmacenarBlobPort blob,
        ILogger<RemoverAdjuntoHandler> logger)
    {
        _db = db;
        _blob = blob;
        _logger = logger;
    }

    public async Task Handle(RemoverAdjuntoCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Adjuntos)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        // El agregado valida estado (Borrador) y existencia del adjunto;
        // devuelve la blob URL para borrar el blob físico.
        var blobUrl = oc.RemoverAdjunto(command.AdjuntoId);
        await _db.SaveChangesAsync(cancellationToken);

        // Borrar el blob físico fuera de la TX de BD. Si falla aquí, la
        // fila ya está borrada — queda un blob huérfano (proceso de
        // limpieza periódica lo recoge, post-MVP).
        try
        {
            await _blob.EliminarAsync(blobUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Adjunto {AdjuntoId} removido de la BD pero falló borrado del blob {BlobUrl}",
                command.AdjuntoId, blobUrl);
        }
    }
}

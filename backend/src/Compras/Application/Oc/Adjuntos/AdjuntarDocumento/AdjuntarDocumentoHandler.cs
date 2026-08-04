using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Adjuntos.AdjuntarDocumento;

/// <summary>
/// Persiste la metadata del adjunto en BD. Valida que el tipo de
/// documento exista y esté activo (cross-table). El blob real lo subió
/// el endpoint antes de invocar este comando.
/// </summary>
public sealed class AdjuntarDocumentoHandler
    : IRequestHandler<AdjuntarDocumentoCommand, AdjuntarDocumentoResponse>
{
    private readonly ComprasDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly IClock _clock;

    public AdjuntarDocumentoHandler(
        ComprasDbContext db,
        ICurrentUserContext currentUser,
        IClock clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<AdjuntarDocumentoResponse> Handle(
        AdjuntarDocumentoCommand command,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("Sin usuario autenticado.");
        }

        var oc = await _db.OrdenesCompra
            .Include(o => o.Adjuntos)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}'.");

        // Validar tipo de documento existe y está activo.
        var tipo = await _db.TiposDocumentoOc
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == command.TipoDocumentoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "TIPO_DOCUMENTO_NO_ENCONTRADO",
                $"No se encontró tipo de documento con id '{command.TipoDocumentoId}'.");

        if (!tipo.Activo)
        {
            throw new BusinessRuleException(
                "TIPO_DOCUMENTO_INACTIVO",
                $"El tipo de documento '{tipo.Clave}' está inactivo y no acepta nuevos adjuntos.");
        }

        var fechaCarga = _clock.UtcNow;
        var adjunto = oc.AdjuntarDocumento(
            adjuntoId: Guid.CreateVersion7(),
            tipoDocumentoId: command.TipoDocumentoId,
            nombreArchivo: command.NombreArchivo,
            blobUrl: command.BlobUrl,
            contentType: command.ContentType,
            tamañoBytes: command.TamañoBytes,
            fechaCarga: fechaCarga,
            usuarioCargaId: userId);

        await _db.SaveChangesAsync(cancellationToken);

        return new AdjuntarDocumentoResponse(adjunto.Id, adjunto.BlobUrl, adjunto.FechaCarga);
    }
}

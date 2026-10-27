using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.RegistrarImpresion;

public sealed record RegistrarImpresionOcCommand(Guid OrdenCompraId, Guid PdfId) : IRequest<int>;

public sealed class RegistrarImpresionOcHandler(
    ComprasDbContext db, ICurrentUserContext user, IClock clock) : IRequestHandler<RegistrarImpresionOcCommand, int>
{
    public async Task<int> Handle(RegistrarImpresionOcCommand request, CancellationToken cancellationToken)
    {
        if (user.UserId is not Guid usuarioId || usuarioId == Guid.Empty)
            throw new UnauthorizedAccessException("Inicia sesión para descargar la orden de compra.");
        var oc = await db.OrdenesCompra.AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == request.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        if (oc.Estado is not (EstadoOrdenCompra.Autorizada or EstadoOrdenCompra.Cerrada))
            throw new BusinessRuleException("OC_PDF_NO_AUTORIZADA", "Autoriza la orden de compra antes de descargar su PDF.");

        // Serializar descargas de la misma versión del PDF. El token de
        // concurrencia también protege frente a escritores sin este bloqueo.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var pdf = await db.OrdenCompraPdfs
            .FromSqlInterpolated($"SELECT * FROM compras.orden_compra_pdf WHERE id = {request.PdfId} AND orden_compra_id = {oc.Id} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("OC_PDF_NO_ENCONTRADO", "El PDF cambió. Actualiza la orden y vuelve a descargarlo.");
        var numero = pdf.RegistrarImpresion(usuarioId, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return numero;
    }
}

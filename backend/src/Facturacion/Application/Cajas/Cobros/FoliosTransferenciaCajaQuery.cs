using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Cobros;

/// <summary>
/// F6.6: folios de transferencia vigentes del cliente del comprobante que se va a
/// cobrar, para que el cajero elija el que le dio Ingresos (forma 03).
/// </summary>
public sealed record FoliosTransferenciaCajaQuery(Guid ComprobanteId) : IRequest<IReadOnlyList<FolioTransferenciaCajaDto>>;

public sealed class FoliosTransferenciaCajaHandler(
    FacturacionDbContext db, IAlcanceCajaEvaluator alcance, IFolioTransferenciaReadPort folios)
    : IRequestHandler<FoliosTransferenciaCajaQuery, IReadOnlyList<FolioTransferenciaCajaDto>>
{
    public async Task<IReadOnlyList<FolioTransferenciaCajaDto>> Handle(
        FoliosTransferenciaCajaQuery query, CancellationToken cancellationToken)
    {
        var resuelto = await alcance.ResolverAsync(cancellationToken);
        var comprobante = await resuelto.AplicarA(db.Comprobantes).AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == query.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante '{query.ComprobanteId}'.");
        return await ClienteContableFacturacion.ResolverAsync(db, comprobante, cancellationToken) is Guid clienteId
            ? await folios.VigentesDelClienteAsync(clienteId, cancellationToken)
            : [];
    }
}

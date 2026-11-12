using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cancelaciones.Queries;

/// <summary>Si la cancelación del comprobante necesita la aceptación del receptor, y por qué no, según la regla fiscal.</summary>
public sealed record AceptacionCancelacionQuery(Guid ComprobanteId) : IRequest<AceptacionCancelacionResponse>;

public sealed record AceptacionCancelacionResponse(bool RequiereAceptacion, IReadOnlyList<string> RazonesSinAceptacion, bool EsGlobal);

public sealed class AceptacionCancelacionHandler(FacturacionDbContext db, IClock clock)
    : IRequestHandler<AceptacionCancelacionQuery, AceptacionCancelacionResponse>
{
    public async Task<AceptacionCancelacionResponse> Handle(AceptacionCancelacionQuery query, CancellationToken cancellationToken)
    {
        var c = await db.Comprobantes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == query.ComprobanteId, cancellationToken)
            ?? throw new EntityNotFoundException("COMPROBANTE_NO_ENCONTRADO", $"No existe el comprobante '{query.ComprobanteId}'.");
        var r = PoliticaAceptacionCancelacion.Evaluar(c, clock.UtcNow);
        return new AceptacionCancelacionResponse(
            r.RequiereAceptacion, r.RazonesSinAceptacion,
            c.ReceptorRfc == Millet.Facturacion.Domain.Comprobantes.DatosFiscalesReceptor.RfcGenericoNacional);
    }
}

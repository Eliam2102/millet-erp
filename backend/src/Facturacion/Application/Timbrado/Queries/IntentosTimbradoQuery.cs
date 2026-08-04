using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Timbrado.Queries;

/// <summary>
/// Historial de intentos de timbrado de un comprobante ([Decisión 01-G] G4,
/// F13). Lee <c>BitacoraIntentoTimbrado</c> por <c>ComprobanteId</c> —
/// una fila por llamada al PAC, la más reciente primero.
/// </summary>
public sealed record IntentosTimbradoQuery(Guid ComprobanteId) : IRequest<IReadOnlyList<IntentoTimbradoItem>>;

public sealed record IntentoTimbradoItem(
    Guid Id,
    int IntentoNumero,
    string Resultado,
    string? ErrorCodigo,
    string? ErrorMensaje,
    DateTimeOffset RegistradoAt);

public sealed class IntentosTimbradoHandler
    : IRequestHandler<IntentosTimbradoQuery, IReadOnlyList<IntentoTimbradoItem>>
{
    private readonly FacturacionDbContext _db;

    public IntentosTimbradoHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<IntentoTimbradoItem>> Handle(
        IntentosTimbradoQuery query, CancellationToken cancellationToken)
    {
        var rows = await _db.BitacorasIntentoTimbrado.AsNoTracking()
            .Where(b => b.ComprobanteId == query.ComprobanteId)
            .OrderByDescending(b => b.IntentoNumero)
            .Select(b => new { b.Id, b.IntentoNumero, b.Resultado, b.ErrorCodigo, b.ErrorMensaje, b.RegistradoAt })
            .ToListAsync(cancellationToken);

        return rows
            .Select(b => new IntentoTimbradoItem(
                b.Id, b.IntentoNumero, b.Resultado.ToString(), b.ErrorCodigo, b.ErrorMensaje, b.RegistradoAt))
            .ToList();
    }
}

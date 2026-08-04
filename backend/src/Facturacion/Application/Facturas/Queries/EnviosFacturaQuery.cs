using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Facturas.Queries;

/// <summary>
/// Bitácora de envíos de CFDI por correo de una factura (B6, FE-F2). Lee
/// <c>BitacoraEnvioCorreo</c> por <c>ComprobanteId</c>.
/// </summary>
public sealed record EnviosFacturaQuery(Guid FacturaId) : IRequest<IReadOnlyList<EnvioCorreoItem>>;

public sealed record EnvioCorreoItem(
    Guid Id,
    string Destinatario,
    string Estado,
    int Intentos,
    DateTimeOffset? EnviadoAt,
    string? UltimoError);

public sealed class EnviosFacturaHandler
    : IRequestHandler<EnviosFacturaQuery, IReadOnlyList<EnvioCorreoItem>>
{
    private readonly FacturacionDbContext _db;

    public EnviosFacturaHandler(FacturacionDbContext db) => _db = db;

    public async Task<IReadOnlyList<EnvioCorreoItem>> Handle(
        EnviosFacturaQuery query, CancellationToken cancellationToken)
    {
        var rows = await _db.BitacorasEnvioCorreo.AsNoTracking()
            .Where(b => b.ComprobanteId == query.FacturaId)
            .OrderByDescending(b => b.EnviadoAt)
            .Select(b => new { b.Id, b.Destinatario, b.Estado, b.Intentos, b.EnviadoAt, b.UltimoError })
            .ToListAsync(cancellationToken);

        return rows
            .Select(b => new EnvioCorreoItem(b.Id, b.Destinatario, b.Estado.ToString(), b.Intentos, b.EnviadoAt, b.UltimoError))
            .ToList();
    }
}
